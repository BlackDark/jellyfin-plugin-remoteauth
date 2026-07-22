using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.RemoteAuth.Auth;
using Jellyfin.Plugin.RemoteAuth.Configuration;
using Jellyfin.Plugin.RemoteAuth.Services;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Jellyfin.Plugin.RemoteAuth.Tests;

public class UserSyncServiceTests
{
    private readonly Mock<IUserManager> _userManager = new();
    private readonly Mock<ILibraryManager> _libraryManager = new();

    private UserSyncService CreateSut(RbacService? rbac = null)
    {
        rbac ??= new RbacService(
            _userManager.Object,
            _libraryManager.Object,
            NullLogger<RbacService>.Instance);

        return new UserSyncService(
            _userManager.Object,
            rbac,
            NullLogger<UserSyncService>.Instance);
    }

    [Fact]
    public async Task UpdateUserResilient_SucceedsOnFirstAttempt()
    {
        var sut = CreateSut();
        var user = CreateUser("alice");
        _userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        _userManager.Setup(m => m.UpdateUserAsync(user)).Returns(Task.CompletedTask);

        await sut.UpdateUserResilientAsync(
            user.Id,
            u => u.AuthenticationProviderId = AuthenticationProviderIds.RemoteAuth);

        Assert.Equal(AuthenticationProviderIds.RemoteAuth, user.AuthenticationProviderId);
        _userManager.Verify(m => m.GetUserById(user.Id), Times.Once);
        _userManager.Verify(m => m.UpdateUserAsync(user), Times.Once);
    }

    [Fact]
    public async Task UpdateUserResilient_RetriesAfterConcurrencyThenSucceeds()
    {
        var sut = CreateSut();
        var user = CreateUser("bob");
        _userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        _userManager
            .SetupSequence(m => m.UpdateUserAsync(user))
            .ThrowsAsync(new DbUpdateConcurrencyException())
            .Returns(Task.CompletedTask);

        await sut.UpdateUserResilientAsync(
            user.Id,
            u => u.AuthenticationProviderId = AuthenticationProviderIds.RemoteAuth);

        Assert.Equal(AuthenticationProviderIds.RemoteAuth, user.AuthenticationProviderId);
        _userManager.Verify(m => m.GetUserById(user.Id), Times.Exactly(2));
        _userManager.Verify(m => m.UpdateUserAsync(user), Times.Exactly(2));
    }

    [Fact]
    public async Task UpdateUserResilient_RethrowsAfterThreeConcurrencyFailures()
    {
        var sut = CreateSut();
        var user = CreateUser("carol");
        _userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        _userManager
            .Setup(m => m.UpdateUserAsync(user))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            sut.UpdateUserResilientAsync(
                user.Id,
                u => u.AuthenticationProviderId = AuthenticationProviderIds.RemoteAuth));

        _userManager.Verify(m => m.GetUserById(user.Id), Times.Exactly(3));
        _userManager.Verify(m => m.UpdateUserAsync(user), Times.Exactly(3));
    }

    [Fact]
    public async Task SyncUser_AllowPasswordLoginFalse_ForcesRemoteAuthProvider()
    {
        var sut = CreateSut(new NoOpRbacService(_userManager.Object, _libraryManager.Object));
        var user = CreateUser("dave");
        user.AuthenticationProviderId = AuthenticationProviderIds.Default;
        _userManager.Setup(m => m.GetUserByName("dave")).Returns(user);
        _userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        _userManager.Setup(m => m.UpdateUserAsync(user)).Returns(Task.CompletedTask);

        var config = new PluginConfiguration { AllowPasswordLogin = false, AutoCreateUsers = true };
        var id = await sut.SyncUserAsync("dave", displayName: null, roles: ["viewer"], config);

        Assert.Equal(user.Id, id);
        Assert.Equal(AuthenticationProviderIds.RemoteAuth, user.AuthenticationProviderId);
        _userManager.Verify(m => m.UpdateUserAsync(user), Times.Once);
    }

    [Fact]
    public async Task SyncUser_AllowPasswordLoginTrue_MigratesRemoteAuthBackToDefault()
    {
        var sut = CreateSut(new NoOpRbacService(_userManager.Object, _libraryManager.Object));
        var user = CreateUser("infuse");
        user.AuthenticationProviderId = AuthenticationProviderIds.RemoteAuth;
        _userManager.Setup(m => m.GetUserByName("infuse")).Returns(user);
        _userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        _userManager.Setup(m => m.UpdateUserAsync(user)).Returns(Task.CompletedTask);

        var config = new PluginConfiguration { AllowPasswordLogin = true };
        await sut.SyncUserAsync("infuse", displayName: null, roles: ["viewer"], config);

        Assert.Equal(AuthenticationProviderIds.Default, user.AuthenticationProviderId);
        _userManager.Verify(m => m.UpdateUserAsync(user), Times.Once);
    }

    [Fact]
    public async Task SyncUser_AllowPasswordLoginTrue_LeavesDefaultProviderUntouched()
    {
        var sut = CreateSut(new NoOpRbacService(_userManager.Object, _libraryManager.Object));
        var user = CreateUser("local");
        user.AuthenticationProviderId = AuthenticationProviderIds.Default;
        _userManager.Setup(m => m.GetUserByName("local")).Returns(user);
        _userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);

        var config = new PluginConfiguration { AllowPasswordLogin = true };
        await sut.SyncUserAsync("local", displayName: null, roles: ["viewer"], config);

        Assert.Equal(AuthenticationProviderIds.Default, user.AuthenticationProviderId);
        _userManager.Verify(m => m.UpdateUserAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task SyncUser_WhenRbacDenies_DoesNotChangeAuthenticationProvider()
    {
        var user = CreateUser("eve");
        user.AuthenticationProviderId = AuthenticationProviderIds.Default;
        _userManager.Setup(m => m.GetUserByName("eve")).Returns(user);

        var sut = CreateSut(new DenyingRbacService(_userManager.Object, _libraryManager.Object));
        var config = new PluginConfiguration { AllowPasswordLogin = false };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.SyncUserAsync("eve", displayName: null, roles: [], config));

        Assert.Contains("denied", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AuthenticationProviderIds.Default, user.AuthenticationProviderId);
        _userManager.Verify(m => m.UpdateUserAsync(It.IsAny<User>()), Times.Never);
    }

    private sealed class NoOpRbacService : RbacService
    {
        public NoOpRbacService(IUserManager userManager, ILibraryManager libraryManager)
            : base(userManager, libraryManager, NullLogger<RbacService>.Instance)
        {
        }

        public override Task ApplyRoleMappingsAsync(Guid userId, string[] userRoles) => Task.CompletedTask;
    }

    private sealed class DenyingRbacService : RbacService
    {
        public DenyingRbacService(IUserManager userManager, ILibraryManager libraryManager)
            : base(userManager, libraryManager, NullLogger<RbacService>.Instance)
        {
        }

        public override Task ApplyRoleMappingsAsync(Guid userId, string[] userRoles)
        {
            throw new InvalidOperationException("No role mapping matched for user 'eve' — denied");
        }
    }

    private static User CreateUser(string username) => new(username, "auth", "reset");

    private sealed class DbUpdateConcurrencyException : Exception;
}
