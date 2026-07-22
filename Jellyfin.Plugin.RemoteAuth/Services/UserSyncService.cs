using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RemoteAuth.Services;

public class UserSyncService
{
    private readonly IUserManager _userManager;
    private readonly RbacService _rbacService;
    private readonly ILogger<UserSyncService> _logger;

    public UserSyncService(
        IUserManager userManager,
        RbacService rbacService,
        ILogger<UserSyncService> logger)
    {
        _userManager = userManager;
        _rbacService = rbacService;
        _logger = logger;
    }

    public async Task<Guid> SyncUserAsync(string username, string? displayName, string[] roles)
    {
        var user = _userManager.GetUserByName(username);

        if (user == null)
        {
            var config = RemoteAuthPlugin.Instance?.Configuration;
            if (config?.AutoCreateUsers != true)
            {
                throw new InvalidOperationException(
                    $"User '{username}' does not exist and auto-creation is disabled");
            }

            user = await _userManager.CreateUserAsync(username).ConfigureAwait(false);

            // Set a random password — nobody will ever use it, login goes through the proxy
            var randomPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            await _userManager.ChangePassword(user.Id, randomPassword).ConfigureAwait(false);

            _logger.LogInformation("Created new Remote Auth user: {Username}", username);
        }

        var userId = user.Id;

        // RBAC first: deny must not leave AuthenticationProviderId switched (password lockout).
        // Permissions persist via UpdatePolicyAsync inside RbacService.
        await _rbacService.ApplyRoleMappingsAsync(userId, roles).ConfigureAwait(false);

        // Only after a successful role match: force Remote Auth provider (blocks password login).
        // CreateUserAsync + ChangePassword advance the concurrency token — re-fetch + retry.
        await UpdateUserResilientAsync(
            userId,
            u => u.AuthenticationProviderId = typeof(Auth.RemoteAuthProvider).FullName!)
            .ConfigureAwait(false);

        return userId;
    }

    internal async Task UpdateUserResilientAsync(Guid userId, Action<User> mutate)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            var user = _userManager.GetUserById(userId)
                ?? throw new InvalidOperationException($"User '{userId}' not found during sync");

            mutate(user);

            try
            {
                await _userManager.UpdateUserAsync(user).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts
                && ex.GetType().Name == "DbUpdateConcurrencyException")
            {
                _logger.LogWarning(
                    "Concurrency conflict updating user {UserId} (attempt {Attempt}); retrying with a fresh copy",
                    userId, attempt);
            }
        }
    }
}
