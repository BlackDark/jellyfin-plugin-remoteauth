using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
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
            user.AuthenticationProviderId = typeof(Auth.RemoteAuthProvider).FullName!;

            // Set a random password — nobody will ever use it, login goes through the proxy
            var randomPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            await _userManager.ChangePassword(user.Id, randomPassword).ConfigureAwait(false);

            _logger.LogInformation("Created new Remote Auth user: {Username}", username);
        }

        user.SetPermission(PermissionKind.IsDisabled, false);
        await _userManager.UpdateUserAsync(user).ConfigureAwait(false);
        await _rbacService.ApplyRoleMappingsAsync(user.Id, roles).ConfigureAwait(false);

        return user.Id;
    }
}
