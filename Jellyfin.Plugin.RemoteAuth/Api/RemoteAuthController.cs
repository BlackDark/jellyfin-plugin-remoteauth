using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Plugin.RemoteAuth.Services;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RemoteAuth.Api;

[ApiController]
[Route("sso/RemoteAuth")]
public class RemoteAuthController : ControllerBase
{
    private readonly UserSyncService _userSyncService;
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<RemoteAuthController> _logger;

    public RemoteAuthController(
        UserSyncService userSyncService,
        ISessionManager sessionManager,
        ILogger<RemoteAuthController> logger)
    {
        _userSyncService = userSyncService;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <summary>
    /// Trusted-header login endpoint. Must be called by the reverse proxy after it has
    /// authenticated the user. The proxy must inject the shared secret header plus the
    /// user identity headers.
    /// </summary>
    [HttpGet("Login")]
    public async Task<ActionResult> Login()
    {
        var config = RemoteAuthPlugin.Instance?.Configuration;

        if (config == null || !config.Enabled)
        {
            return StatusCode(503, "Remote Auth plugin is disabled");
        }

        // Validate shared secret — use constant-time comparison to prevent timing attacks
        if (string.IsNullOrWhiteSpace(config.SecretHeaderValue))
        {
            _logger.LogWarning("RemoteAuth: SecretHeaderValue not configured — refusing all requests");
            return StatusCode(503, "Remote Auth is not configured (missing secret)");
        }

        var secretHeaderName = string.IsNullOrWhiteSpace(config.SecretHeaderName)
            ? "X-Remote-Auth-Secret"
            : config.SecretHeaderName;

        var incomingSecret = Request.Headers[secretHeaderName].ToString();

        if (!ConstantTimeEquals(incomingSecret, config.SecretHeaderValue))
        {
            _logger.LogWarning("RemoteAuth: invalid or missing secret header from {RemoteIp}",
                HttpContext.Connection.RemoteIpAddress);
            return Unauthorized("Invalid or missing authentication secret");
        }

        // Read identity headers
        var userHeader = string.IsNullOrWhiteSpace(config.UserHeader) ? "X-Remote-Auth-User" : config.UserHeader;
        var username = Request.Headers[userHeader].ToString();

        if (string.IsNullOrWhiteSpace(username))
        {
            _logger.LogWarning("RemoteAuth: username header '{Header}' missing or empty", userHeader);
            return Unauthorized("Username header is missing");
        }

        var emailHeader = string.IsNullOrWhiteSpace(config.EmailHeader) ? "X-Remote-Auth-Email" : config.EmailHeader;
        var nameHeader = string.IsNullOrWhiteSpace(config.DisplayNameHeader) ? "X-Remote-Auth-Name" : config.DisplayNameHeader;
        var groupsHeader = string.IsNullOrWhiteSpace(config.GroupsHeader) ? "X-Remote-Auth-Groups" : config.GroupsHeader;
        var delimiter = string.IsNullOrWhiteSpace(config.GroupsDelimiter) ? "|" : config.GroupsDelimiter;

        var displayName = Request.Headers[nameHeader].ToString();
        var groupsRaw = Request.Headers[groupsHeader].ToString();

        var roles = string.IsNullOrWhiteSpace(groupsRaw)
            ? Array.Empty<string>()
            : groupsRaw.Split(delimiter, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        _logger.LogInformation("RemoteAuth: authenticating user={Username}, groups=[{Groups}]",
            username, string.Join(", ", roles));

        try
        {
            var userId = await _userSyncService.SyncUserAsync(username, displayName, roles).ConfigureAwait(false);

            var deviceId = Request.Headers["X-Device-Id"].ToString();
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                deviceId = Guid.NewGuid().ToString();
            }

            var authRequest = new AuthenticationRequest
            {
                App = "Jellyfin Web",
                AppVersion = "10.11.0",
                DeviceId = deviceId,
                DeviceName = "RemoteAuth",
                UserId = userId
            };

            var authResult = await _sessionManager.AuthenticateDirect(authRequest).ConfigureAwait(false);

            return Content(BuildSuccessHtml(authResult), "text/html");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("RemoteAuth: user sync failed for {Username}: {Message}", username, ex.Message);
            return StatusCode(403, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RemoteAuth: authentication failed for user {Username}", username);
            return StatusCode(500, "Authentication failed");
        }
    }

    /// <summary>
    /// Constant-time string comparison to prevent timing-based secret inference.
    /// Note: the early length check does leak whether lengths match, but this is
    /// unavoidable without HMAC. For a shared secret this is acceptable — the
    /// important property is that same-length secrets cannot be brute-forced
    /// character-by-character via timing.
    /// </summary>
    private static bool ConstantTimeEquals(string a, string b)
    {
        if (a == null || b == null)
        {
            return false;
        }

        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);

        if (aBytes.Length != bBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }

    private static string BuildSuccessHtml(AuthenticationResult authResult)
    {
        var accessToken = System.Text.Json.JsonSerializer.Serialize(authResult.AccessToken);
        var userId = System.Text.Json.JsonSerializer.Serialize(authResult.User.Id.ToString());
        var serverId = System.Text.Json.JsonSerializer.Serialize(authResult.ServerId);

        return $$"""
        <!DOCTYPE html>
        <html>
        <head><title>Authenticating...</title></head>
        <body>
        <p id="status">Completing authentication, please wait...</p>
        <script>
        (function() {
            var accessToken = {{accessToken}};
            var userId = {{userId}};
            var serverId = {{serverId}};

            var credentials = {
                Servers: [{
                    ManualAddress: window.location.origin,
                    AccessToken: accessToken,
                    UserId: userId,
                    IsLocalUser: true
                }]
            };
            localStorage.setItem('jellyfin_credentials', JSON.stringify(credentials));

            var user = {
                Id: userId,
                ServerId: serverId,
                AccessToken: accessToken
            };
            localStorage.setItem('_jellyfin_user_' + serverId, JSON.stringify(user));

            document.getElementById('status').textContent = 'Done! Redirecting...';
            window.location.href = '/';
        })();
        </script>
        </body>
        </html>
        """;
    }
}
