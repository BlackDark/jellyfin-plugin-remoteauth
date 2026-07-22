using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.RemoteAuth.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Users;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RemoteAuth.Services;

public class RbacService
{
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<RbacService> _logger;

    public RbacService(
        IUserManager userManager,
        ILibraryManager libraryManager,
        ILogger<RbacService> logger)
    {
        _userManager = userManager;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    public Task ApplyRoleMappingsAsync(Guid userId, string[] userRoles)
    {
        var config = RemoteAuthPlugin.Instance?.Configuration;
        if (config == null)
        {
            return Task.CompletedTask;
        }

        return ApplyRoleMappingsAsync(userId, userRoles, config);
    }

    internal async Task ApplyRoleMappingsAsync(Guid userId, string[] userRoles, PluginConfiguration config)
    {
        var user = _userManager.GetUserById(userId);
        if (user == null)
        {
            _logger.LogWarning("User {UserId} not found for RBAC application", userId);
            return;
        }

        var match = RoleMatchResolver.Resolve(
            config.RoleMappings,
            config.DefaultRoleName,
            config.AdminGroup,
            userRoles);

        if (match.Kind == RoleMatchKind.Deny)
        {
            if (match.RevokeAdminShortcut)
            {
                var revokePolicy = _userManager.GetUserDto(user).Policy;
                revokePolicy.IsAdministrator = false;
                revokePolicy.EnableAllFolders = false;
                revokePolicy.EnabledFolders = Array.Empty<Guid>();
                await _userManager.UpdatePolicyAsync(userId, revokePolicy).ConfigureAwait(false);
                _logger.LogInformation(
                    "Revoked AdminGroup admin for user {Username} (group={AdminGroup})",
                    user.Username,
                    config.AdminGroup);
            }
            else
            {
                _logger.LogInformation(
                    "No role mappings matched for user {Username} with roles [{Roles}]",
                    user.Username,
                    string.Join(", ", userRoles));
            }

            throw new InvalidOperationException(
                $"No role mapping matched for user '{user.Username}'");
        }

        // Persistence MUST go through UpdatePolicyAsync: UpdateUserAsync only writes the root User
        // row and silently drops Permission/Preference changes on Jellyfin 10.11+.
        var policy = _userManager.GetUserDto(user).Policy;
        policy.IsDisabled = false;

        if (match.Kind == RoleMatchKind.AdminGroupOnly)
        {
            policy.IsAdministrator = true;
            policy.EnableAllFolders = true;
            policy.EnabledFolders = Array.Empty<Guid>();
            await _userManager.UpdatePolicyAsync(userId, policy).ConfigureAwait(false);
            _logger.LogInformation(
                "Applied AdminGroup shortcut for user {Username}: admin=true (group={AdminGroup})",
                user.Username,
                config.AdminGroup);
            return;
        }

        var matchedMappings = match.Mappings.ToList();
        var merged = MergeMappings(matchedMappings);
        var isAdmin = merged.IsAdmin || IsAdminGroupMember(config, userRoles);

        policy.IsAdministrator = isAdmin;
        policy.EnableMediaPlayback = merged.EnableMediaPlayback;
        policy.EnableRemoteAccess = merged.EnableRemoteAccess;
        policy.EnableAudioPlaybackTranscoding = merged.EnableTranscoding;
        policy.EnableVideoPlaybackTranscoding = merged.EnableTranscoding;
        policy.EnableLiveTvAccess = merged.EnableLiveTv;
        policy.EnableLiveTvManagement = merged.EnableLiveTvManagement;
        policy.EnableContentDeletion = merged.EnableContentDeletion;
        policy.EnableCollectionManagement = merged.EnableCollectionManagement;
        policy.EnableSubtitleManagement = merged.EnableSubtitleManagement;

        if (isAdmin || merged.EnableAllLibraries)
        {
            policy.EnableAllFolders = true;
            policy.EnabledFolders = Array.Empty<Guid>();
        }
        else
        {
            policy.EnableAllFolders = false;
            policy.EnabledFolders = ResolveLibraryIds(merged.LibraryIds, merged.LibraryNames)
                .Select(id => Guid.TryParse(id, out var g) ? (Guid?)g : null)
                .Where(g => g.HasValue)
                .Select(g => g!.Value)
                .ToArray();
        }

        if (merged.MaxParentalRating.HasValue)
        {
            policy.MaxParentalRating = merged.MaxParentalRating;
        }

        await _userManager.UpdatePolicyAsync(userId, policy).ConfigureAwait(false);

        _logger.LogInformation(
            "Applied RBAC for user {Username}: admin={IsAdmin}, libraries={LibraryCount}, roles matched=[{Roles}]",
            user.Username,
            isAdmin,
            isAdmin || merged.EnableAllLibraries ? "ALL" : policy.EnabledFolders.Length.ToString(),
            string.Join(", ", matchedMappings.Select(m => m.RoleName)));
    }

    public Dictionary<string, string> GetAvailableLibraries()
    {
        var folders = _libraryManager.GetVirtualFolders();
        return folders.ToDictionary(
            f => f.ItemId,
            f => f.Name);
    }

    private List<string> ResolveLibraryIds(List<string> ids, List<string> names)
    {
        var resolved = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0)
        {
            return resolved.ToList();
        }

        var folders = _libraryManager.GetVirtualFolders();
        foreach (var name in names)
        {
            var folder = folders.FirstOrDefault(f =>
                string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (folder != null)
            {
                resolved.Add(folder.ItemId);
            }
            else
            {
                _logger.LogWarning("Library '{LibraryName}' not found during RBAC resolution", name);
            }
        }

        return resolved.ToList();
    }

    private static bool IsAdminGroupMember(PluginConfiguration config, string[] userRoles)
    {
        return !string.IsNullOrWhiteSpace(config.AdminGroup)
            && userRoles.Contains(config.AdminGroup, StringComparer.OrdinalIgnoreCase);
    }

    private static RoleMapping MergeMappings(List<RoleMapping> mappings)
    {
        return new RoleMapping
        {
            IsAdmin = mappings.Any(m => m.IsAdmin),
            EnableAllLibraries = mappings.Any(m => m.EnableAllLibraries),
            EnableLiveTv = mappings.Any(m => m.EnableLiveTv),
            EnableLiveTvManagement = mappings.Any(m => m.EnableLiveTvManagement),
            EnableMediaPlayback = mappings.Any(m => m.EnableMediaPlayback),
            EnableRemoteAccess = mappings.Any(m => m.EnableRemoteAccess),
            EnableTranscoding = mappings.Any(m => m.EnableTranscoding),
            EnableContentDeletion = mappings.Any(m => m.EnableContentDeletion),
            EnableCollectionManagement = mappings.Any(m => m.EnableCollectionManagement),
            EnableSubtitleManagement = mappings.Any(m => m.EnableSubtitleManagement),
            MaxParentalRating = mappings.Select(m => m.MaxParentalRating).Max(),
            LibraryIds = mappings
                .SelectMany(m => m.LibraryIds)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            LibraryNames = mappings
                .SelectMany(m => m.LibraryNames)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }
}
