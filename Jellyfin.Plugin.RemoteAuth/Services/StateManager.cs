using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RemoteAuth.Services;

public sealed class AuthorizedSession
{
    public const int MaxFailedCodeAttempts = 5;

    public required string Username { get; init; }
    public string? DisplayName { get; init; }
    public required string[] Roles { get; init; }
    public required Guid UserId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Failed Quick Connect code attempts; session invalidated at <see cref="MaxFailedCodeAttempts"/>.</summary>
    public int FailedCodeAttempts { get; set; }
}

public sealed class StateManager : IHostedService, IDisposable
{
    private static readonly TimeSpan SessionExpiry = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<string, AuthorizedSession> _authorizedSessions = new();
    private readonly ILogger<StateManager> _logger;
    private Timer? _cleanupTimer;

    public StateManager(ILogger<StateManager> logger)
    {
        _logger = logger;
    }

    public string CreateAuthorizedSession(AuthorizedSession session)
    {
        var token = Guid.NewGuid().ToString("N");
        _authorizedSessions[token] = session;
        return token;
    }

    /// <summary>
    /// Returns the authorized session without removing it, so a caller can validate it across
    /// multiple attempts (e.g. a mistyped Quick Connect code). Expired sessions are evicted and
    /// return null. Invalidate explicitly with <see cref="InvalidateAuthorizedSession"/> once done.
    /// </summary>
    public AuthorizedSession? PeekAuthorizedSession(string token)
    {
        if (!_authorizedSessions.TryGetValue(token, out var session))
        {
            return null;
        }

        if (DateTimeOffset.UtcNow - session.CreatedAt > SessionExpiry)
        {
            _authorizedSessions.TryRemove(token, out _);
            _logger.LogWarning("Authorized session expired for user {Username}", session.Username);
            return null;
        }

        return session;
    }

    public void InvalidateAuthorizedSession(string token)
    {
        _authorizedSessions.TryRemove(token, out _);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cleanupTimer = new Timer(Cleanup, null, CleanupInterval, CleanupInterval);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cleanupTimer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }

    private void Cleanup(object? state)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var (key, session) in _authorizedSessions)
        {
            if (now - session.CreatedAt > SessionExpiry)
            {
                _authorizedSessions.TryRemove(key, out _);
            }
        }
    }
}
