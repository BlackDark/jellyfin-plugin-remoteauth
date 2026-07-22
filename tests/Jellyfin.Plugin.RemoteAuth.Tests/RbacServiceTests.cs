using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.RemoteAuth.Configuration;
using Jellyfin.Plugin.RemoteAuth.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Jellyfin.Plugin.RemoteAuth.Tests;

public class RbacServiceTests
{
    private readonly Mock<IUserManager> _userManager = new();
    private readonly Mock<ILibraryManager> _libraryManager = new();
    private readonly RbacService _sut;

    public RbacServiceTests()
    {
        _sut = new RbacService(
            _userManager.Object,
            _libraryManager.Object,
            NullLogger<RbacService>.Instance);
    }

    [Fact]
    public async Task Apply_Deny_ThrowsWithoutUpdatingPolicy()
    {
        var user = CreateUser("alice");
        SetupUser(user, new UserPolicy { IsAdministrator = true });

        var config = new PluginConfiguration
        {
            RoleMappings = [new RoleMapping { RoleName = "viewer" }],
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.ApplyRoleMappingsAsync(user.Id, ["other"], config));

        Assert.Contains("'alice'", ex.Message);
        _userManager.Verify(
            m => m.UpdatePolicyAsync(It.IsAny<Guid>(), It.IsAny<UserPolicy>()),
            Times.Never);
    }

    [Fact]
    public async Task Apply_DenyWithRevoke_PersistsRevokeThenThrows()
    {
        var user = CreateUser("bob");
        SetupUser(user, new UserPolicy
        {
            IsAdministrator = true,
            EnableAllFolders = true,
            EnabledFolders = [Guid.NewGuid()],
        });

        UserPolicy? saved = null;
        _userManager
            .Setup(m => m.UpdatePolicyAsync(user.Id, It.IsAny<UserPolicy>()))
            .Callback<Guid, UserPolicy>((_, p) => saved = p)
            .Returns(Task.CompletedTask);

        var config = new PluginConfiguration
        {
            AdminGroup = "jellyfin-admins",
            RoleMappings = [],
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.ApplyRoleMappingsAsync(user.Id, ["users"], config));

        Assert.NotNull(saved);
        Assert.False(saved!.IsAdministrator);
        Assert.False(saved.EnableAllFolders);
        Assert.Empty(saved.EnabledFolders);
        _userManager.Verify(m => m.UpdatePolicyAsync(user.Id, It.IsAny<UserPolicy>()), Times.Once);
    }

    [Fact]
    public async Task Apply_AdminGroupOnly_SetsAdminAllFoldersAndEnables()
    {
        var user = CreateUser("carol");
        SetupUser(user, new UserPolicy { IsDisabled = true, IsAdministrator = false });

        UserPolicy? saved = null;
        _userManager
            .Setup(m => m.UpdatePolicyAsync(user.Id, It.IsAny<UserPolicy>()))
            .Callback<Guid, UserPolicy>((_, p) => saved = p)
            .Returns(Task.CompletedTask);

        var config = new PluginConfiguration
        {
            AdminGroup = "jellyfin-admins",
        };

        await _sut.ApplyRoleMappingsAsync(user.Id, ["jellyfin-admins"], config);

        Assert.NotNull(saved);
        Assert.False(saved!.IsDisabled);
        Assert.True(saved.IsAdministrator);
        Assert.True(saved.EnableAllFolders);
        Assert.Empty(saved.EnabledFolders);
    }

    [Fact]
    public async Task Apply_Matched_PersistsMergedPolicyViaUpdatePolicyAsync()
    {
        var user = CreateUser("dave");
        SetupUser(user, new UserPolicy { IsDisabled = true });

        UserPolicy? saved = null;
        _userManager
            .Setup(m => m.UpdatePolicyAsync(user.Id, It.IsAny<UserPolicy>()))
            .Callback<Guid, UserPolicy>((_, p) => saved = p)
            .Returns(Task.CompletedTask);

        var libId = Guid.NewGuid();
        var config = new PluginConfiguration
        {
            RoleMappings =
            [
                new RoleMapping
                {
                    RoleName = "viewer",
                    Priority = 1,
                    EnableMediaPlayback = true,
                    EnableRemoteAccess = true,
                    EnableTranscoding = false,
                    LibraryIds = [libId.ToString()],
                    MaxParentalRating = 12,
                },
            ],
        };

        await _sut.ApplyRoleMappingsAsync(user.Id, ["viewer"], config);

        Assert.NotNull(saved);
        Assert.False(saved!.IsDisabled);
        Assert.False(saved.IsAdministrator);
        Assert.False(saved.EnableAllFolders);
        Assert.Equal([libId], saved.EnabledFolders);
        Assert.True(saved.EnableMediaPlayback);
        Assert.True(saved.EnableRemoteAccess);
        Assert.False(saved.EnableAudioPlaybackTranscoding);
        Assert.False(saved.EnableVideoPlaybackTranscoding);
        Assert.Equal(12, saved.MaxParentalRating);
        _userManager.Verify(m => m.UpdateUserAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task Apply_MatchedAdminOrEnableAllLibraries_ForcesAllFolders()
    {
        var user = CreateUser("erin");
        SetupUser(user, new UserPolicy());

        UserPolicy? saved = null;
        _userManager
            .Setup(m => m.UpdatePolicyAsync(user.Id, It.IsAny<UserPolicy>()))
            .Callback<Guid, UserPolicy>((_, p) => saved = p)
            .Returns(Task.CompletedTask);

        var config = new PluginConfiguration
        {
            RoleMappings =
            [
                new RoleMapping
                {
                    RoleName = "admin",
                    IsAdmin = true,
                    LibraryIds = [Guid.NewGuid().ToString()],
                },
            ],
        };

        await _sut.ApplyRoleMappingsAsync(user.Id, ["admin"], config);

        Assert.NotNull(saved);
        Assert.True(saved!.IsAdministrator);
        Assert.True(saved.EnableAllFolders);
        Assert.Empty(saved.EnabledFolders);
    }

    [Fact]
    public async Task Apply_MatchedPlusAdminGroup_ForcesAdmin()
    {
        var user = CreateUser("frank");
        SetupUser(user, new UserPolicy());

        UserPolicy? saved = null;
        _userManager
            .Setup(m => m.UpdatePolicyAsync(user.Id, It.IsAny<UserPolicy>()))
            .Callback<Guid, UserPolicy>((_, p) => saved = p)
            .Returns(Task.CompletedTask);

        var config = new PluginConfiguration
        {
            AdminGroup = "jellyfin-admins",
            RoleMappings =
            [
                new RoleMapping { RoleName = "viewer", IsAdmin = false },
            ],
        };

        await _sut.ApplyRoleMappingsAsync(user.Id, ["viewer", "jellyfin-admins"], config);

        Assert.NotNull(saved);
        Assert.True(saved!.IsAdministrator);
        Assert.True(saved.EnableAllFolders);
        Assert.Empty(saved.EnabledFolders);
    }

    private void SetupUser(User user, UserPolicy policy)
    {
        _userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        _userManager.Setup(m => m.GetUserDto(user, It.IsAny<string?>())).Returns(new UserDto
        {
            Id = user.Id,
            Name = user.Username,
            Policy = policy,
        });
    }

    private static User CreateUser(string username) => new(username, "auth", "reset");
}
