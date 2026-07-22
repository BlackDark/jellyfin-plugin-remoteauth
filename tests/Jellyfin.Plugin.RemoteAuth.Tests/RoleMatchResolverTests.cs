using Jellyfin.Plugin.RemoteAuth.Configuration;
using Jellyfin.Plugin.RemoteAuth.Services;

namespace Jellyfin.Plugin.RemoteAuth.Tests;

public class RoleMatchResolverTests
{
    [Fact]
    public void Resolve_MatchingRoles_ReturnsMatchedOrderedByPriorityDesc()
    {
        var mappings = new List<RoleMapping>
        {
            new() { RoleName = "viewer", Priority = 1 },
            new() { RoleName = "editor", Priority = 10 },
            new() { RoleName = "admin", Priority = 5 },
        };

        var result = RoleMatchResolver.Resolve(
            mappings,
            defaultRoleName: null,
            adminGroup: null,
            userRoles: ["editor", "viewer"]);

        Assert.Equal(RoleMatchKind.Matched, result.Kind);
        Assert.Equal(["editor", "viewer"], result.Mappings.Select(m => m.RoleName));
        Assert.False(result.RevokeAdminShortcut);
    }

    [Fact]
    public void Resolve_NoMatch_UsesDefaultRoleMapping()
    {
        var mappings = new List<RoleMapping>
        {
            new() { RoleName = "guest", Priority = 1 },
        };

        var result = RoleMatchResolver.Resolve(
            mappings,
            defaultRoleName: "guest",
            adminGroup: null,
            userRoles: ["unrelated"]);

        Assert.Equal(RoleMatchKind.Matched, result.Kind);
        Assert.Single(result.Mappings);
        Assert.Equal("guest", result.Mappings[0].RoleName);
    }

    [Fact]
    public void Resolve_NoMatch_AdminGroupMember_ReturnsAdminGroupOnly()
    {
        var result = RoleMatchResolver.Resolve(
            roleMappings: [],
            defaultRoleName: null,
            adminGroup: "jellyfin-admins",
            userRoles: ["jellyfin-admins"]);

        Assert.Equal(RoleMatchKind.AdminGroupOnly, result.Kind);
        Assert.Empty(result.Mappings);
        Assert.False(result.RevokeAdminShortcut);
    }

    [Fact]
    public void Resolve_NoMatch_NoAdminGroup_ReturnsDeny()
    {
        var result = RoleMatchResolver.Resolve(
            roleMappings: [new RoleMapping { RoleName = "viewer" }],
            defaultRoleName: null,
            adminGroup: null,
            userRoles: ["other"]);

        Assert.Equal(RoleMatchKind.Deny, result.Kind);
        Assert.False(result.RevokeAdminShortcut);
    }

    [Fact]
    public void Resolve_NoMatch_AdminGroupSetButNotMember_ReturnsDenyWithRevokeFlag()
    {
        var result = RoleMatchResolver.Resolve(
            roleMappings: [],
            defaultRoleName: null,
            adminGroup: "jellyfin-admins",
            userRoles: ["users"]);

        Assert.Equal(RoleMatchKind.Deny, result.Kind);
        Assert.True(result.RevokeAdminShortcut);
    }

    [Fact]
    public void Resolve_MatchIsCaseInsensitive()
    {
        var mappings = new List<RoleMapping>
        {
            new() { RoleName = "Viewer", Priority = 1 },
        };

        var result = RoleMatchResolver.Resolve(
            mappings,
            defaultRoleName: null,
            adminGroup: null,
            userRoles: ["viewer"]);

        Assert.Equal(RoleMatchKind.Matched, result.Kind);
        Assert.Single(result.Mappings);
    }
}
