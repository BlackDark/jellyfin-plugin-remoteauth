using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.RemoteAuth.Auth;
using Jellyfin.Plugin.RemoteAuth.Services;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Jellyfin.Plugin.RemoteAuth.Tests;

public class UserSyncServiceTests
{
    private readonly Mock<IUserManager> _userManager = new();
    private readonly Mock<ILibraryManager> _libraryManager = new();
    private readonly UserSyncService _sut;

    public UserSyncServiceTests()
    {
        var rbac = new RbacService(
            _userManager.Object,
            _libraryManager.Object,
            NullLogger<RbacService>.Instance);

        _sut = new UserSyncService(
            _userManager.Object,
            rbac,
            NullLogger<UserSyncService>.Instance);
    }

    [Fact]
    public async Task UpdateUserResilient_SucceedsOnFirstAttempt()
    {
        var user = CreateUser("alice");
        _userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        _userManager.Setup(m => m.UpdateUserAsync(user)).Returns(Task.CompletedTask);

        await _sut.UpdateUserResilientAsync(
            user.Id,
            u => u.AuthenticationProviderId = typeof(RemoteAuthProvider).FullName!);

        Assert.Equal(typeof(RemoteAuthProvider).FullName, user.AuthenticationProviderId);
        _userManager.Verify(m => m.GetUserById(user.Id), Times.Once);
        _userManager.Verify(m => m.UpdateUserAsync(user), Times.Once);
    }

    [Fact]
    public async Task UpdateUserResilient_RetriesAfterConcurrencyThenSucceeds()
    {
        var user = CreateUser("bob");
        _userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        _userManager
            .SetupSequence(m => m.UpdateUserAsync(user))
            .ThrowsAsync(new DbUpdateConcurrencyException())
            .Returns(Task.CompletedTask);

        await _sut.UpdateUserResilientAsync(
            user.Id,
            u => u.AuthenticationProviderId = typeof(RemoteAuthProvider).FullName!);

        Assert.Equal(typeof(RemoteAuthProvider).FullName, user.AuthenticationProviderId);
        _userManager.Verify(m => m.GetUserById(user.Id), Times.Exactly(2));
        _userManager.Verify(m => m.UpdateUserAsync(user), Times.Exactly(2));
    }

    [Fact]
    public async Task UpdateUserResilient_RethrowsAfterThreeConcurrencyFailures()
    {
        var user = CreateUser("carol");
        _userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        _userManager
            .Setup(m => m.UpdateUserAsync(user))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            _sut.UpdateUserResilientAsync(
                user.Id,
                u => u.AuthenticationProviderId = typeof(RemoteAuthProvider).FullName!));

        _userManager.Verify(m => m.GetUserById(user.Id), Times.Exactly(3));
        _userManager.Verify(m => m.UpdateUserAsync(user), Times.Exactly(3));
    }

    [Fact]
    public async Task SyncUser_ExistingUser_SetsProviderViaResilientUpdate()
    {
        var user = CreateUser("dave");
        user.AuthenticationProviderId = "old-provider";
        _userManager.Setup(m => m.GetUserByName("dave")).Returns(user);
        _userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        _userManager
            .SetupSequence(m => m.UpdateUserAsync(user))
            .ThrowsAsync(new DbUpdateConcurrencyException())
            .Returns(Task.CompletedTask);

        // Plugin.Instance null → RBAC no-ops; still proves provider resilient path ran.
        var id = await _sut.SyncUserAsync("dave", displayName: null, roles: ["viewer"]);

        Assert.Equal(user.Id, id);
        Assert.Equal(typeof(RemoteAuthProvider).FullName, user.AuthenticationProviderId);
        _userManager.Verify(m => m.GetUserById(user.Id), Times.Exactly(2));
        _userManager.Verify(m => m.UpdateUserAsync(user), Times.Exactly(2));
    }

    private static User CreateUser(string username) => new(username, "auth", "reset");

    /// <summary>
    /// Stand-in matching EF Core exception type name (caught by name, not assembly).
    /// </summary>
    private sealed class DbUpdateConcurrencyException : Exception;
}
