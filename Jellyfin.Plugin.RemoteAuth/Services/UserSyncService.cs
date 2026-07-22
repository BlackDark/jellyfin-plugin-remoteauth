using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.RemoteAuth.Auth;
using Jellyfin.Plugin.RemoteAuth.Configuration;
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

    public Task<Guid> SyncUserAsync(string username, string? displayName, string[] roles)
    {
        var config = RemoteAuthPlugin.Instance?.Configuration
            ?? throw new InvalidOperationException("Remote Auth plugin is not loaded");

        return SyncUserAsync(username, displayName, roles, config);
    }

    internal async Task<Guid> SyncUserAsync(
        string username,
        string? displayName,
        string[] roles,
        PluginConfiguration config)
    {
        var user = _userManager.GetUserByName(username);

        if (user == null)
        {
            if (config.AutoCreateUsers != true)
            {
                throw new InvalidOperationException(
                    $"User '{username}' does not exist and auto-creation is disabled");
            }

            user = await _userManager.CreateUserAsync(username).ConfigureAwait(false);

            // Standing password for hybrid / Infuse. User (or admin) must set a known password
            // in Jellyfin if they need AuthenticateByName — random value is unknown by design.
            var randomPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            await _userManager.ChangePassword(user.Id, randomPassword).ConfigureAwait(false);

            _logger.LogInformation("Created new Remote Auth user: {Username}", username);
        }

        var userId = user.Id;

        // RBAC first: deny must not leave AuthenticationProviderId switched.
        await _rbacService.ApplyRoleMappingsAsync(userId, roles).ConfigureAwait(false);

        await ApplyAuthenticationProviderAsync(userId, config).ConfigureAwait(false);

        return userId;
    }

    private async Task ApplyAuthenticationProviderAsync(Guid userId, PluginConfiguration config)
    {
        var remoteAuthId = AuthenticationProviderIds.RemoteAuth;
        var defaultId = AuthenticationProviderIds.Default;

        var current = _userManager.GetUserById(userId)
            ?? throw new InvalidOperationException($"User '{userId}' not found during sync");

        string? desired;
        if (config.AllowPasswordLogin)
        {
            // Hybrid: migrate RemoteAuth-locked users back to Default so Infuse works.
            // Leave other providers (custom) untouched.
            desired = string.Equals(current.AuthenticationProviderId, remoteAuthId, StringComparison.Ordinal)
                ? defaultId
                : null;
        }
        else
        {
            desired = remoteAuthId;
        }

        if (desired == null
            || string.Equals(current.AuthenticationProviderId, desired, StringComparison.Ordinal))
        {
            return;
        }

        await UpdateUserResilientAsync(
            userId,
            u => u.AuthenticationProviderId = desired)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Set AuthenticationProviderId for user {UserId} to {Provider} (AllowPasswordLogin={AllowPasswordLogin})",
            userId,
            desired,
            config.AllowPasswordLogin);
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
