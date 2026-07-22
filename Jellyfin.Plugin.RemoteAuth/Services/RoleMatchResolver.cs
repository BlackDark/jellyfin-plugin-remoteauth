using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.RemoteAuth.Configuration;

namespace Jellyfin.Plugin.RemoteAuth.Services;

public enum RoleMatchKind
{
    Matched,
    AdminGroupOnly,
    Deny
}

public sealed class RoleMatchResult
{
    public RoleMatchKind Kind { get; init; }

    public IReadOnlyList<RoleMapping> Mappings { get; init; } = Array.Empty<RoleMapping>();

    /// <summary>
    /// True when AdminGroup is configured, user is not a member, and no role mappings matched.
    /// RbacService should revoke admin shortcut before denying.
    /// </summary>
    public bool RevokeAdminShortcut { get; init; }
}

public static class RoleMatchResolver
{
    public static RoleMatchResult Resolve(
        IReadOnlyList<RoleMapping> roleMappings,
        string? defaultRoleName,
        string? adminGroup,
        IReadOnlyList<string> userRoles)
    {
        var matched = roleMappings
            .Where(m => userRoles.Contains(m.RoleName, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(m => m.Priority)
            .ToList();

        if (matched.Count == 0 && !string.IsNullOrEmpty(defaultRoleName))
        {
            var defaultMapping = roleMappings
                .FirstOrDefault(m => string.Equals(m.RoleName, defaultRoleName, StringComparison.OrdinalIgnoreCase));
            if (defaultMapping != null)
            {
                matched.Add(defaultMapping);
            }
        }

        if (matched.Count > 0)
        {
            return new RoleMatchResult
            {
                Kind = RoleMatchKind.Matched,
                Mappings = matched
            };
        }

        var adminConfigured = !string.IsNullOrWhiteSpace(adminGroup);
        var isAdminMember = adminConfigured
            && userRoles.Contains(adminGroup!, StringComparer.OrdinalIgnoreCase);

        if (isAdminMember)
        {
            return new RoleMatchResult { Kind = RoleMatchKind.AdminGroupOnly };
        }

        return new RoleMatchResult
        {
            Kind = RoleMatchKind.Deny,
            RevokeAdminShortcut = adminConfigured
        };
    }
}
