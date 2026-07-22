using Jellyfin.Plugin.RemoteAuth.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.RemoteAuth.Tests;

public class StateManagerTests
{
    private static StateManager CreateSut() => new(NullLogger<StateManager>.Instance);

    [Fact]
    public void CreateAuthorizedSession_Peek_ReturnsSameSession()
    {
        var sut = CreateSut();
        var userId = Guid.NewGuid();
        var token = sut.CreateAuthorizedSession(new AuthorizedSession
        {
            Username = "alice",
            DisplayName = "Alice",
            Roles = ["admin", "users"],
            UserId = userId
        });

        var peeked = sut.PeekAuthorizedSession(token);

        Assert.NotNull(peeked);
        Assert.Equal("alice", peeked.Username);
        Assert.Equal("Alice", peeked.DisplayName);
        Assert.Equal(["admin", "users"], peeked.Roles);
        Assert.Equal(userId, peeked.UserId);
    }

    [Fact]
    public void PeekAuthorizedSession_UnknownToken_ReturnsNull()
    {
        var sut = CreateSut();

        Assert.Null(sut.PeekAuthorizedSession("missing"));
    }

    [Fact]
    public void InvalidateAuthorizedSession_RemovesToken()
    {
        var sut = CreateSut();
        var token = sut.CreateAuthorizedSession(new AuthorizedSession
        {
            Username = "bob",
            Roles = [],
            UserId = Guid.NewGuid()
        });

        sut.InvalidateAuthorizedSession(token);

        Assert.Null(sut.PeekAuthorizedSession(token));
    }

    [Fact]
    public void PeekAuthorizedSession_AfterInvalidate_ReturnsNull()
    {
        var sut = CreateSut();
        var token = sut.CreateAuthorizedSession(new AuthorizedSession
        {
            Username = "carol",
            Roles = ["users"],
            UserId = Guid.NewGuid()
        });

        Assert.NotNull(sut.PeekAuthorizedSession(token));
        sut.InvalidateAuthorizedSession(token);
        Assert.Null(sut.PeekAuthorizedSession(token));
    }
}
