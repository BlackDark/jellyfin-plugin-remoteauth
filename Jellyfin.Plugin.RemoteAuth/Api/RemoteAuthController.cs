using System;
using System.Threading.Tasks;
using Jellyfin.Plugin.RemoteAuth.Auth;
using Jellyfin.Plugin.RemoteAuth.Services;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.QuickConnect;
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
    private readonly IQuickConnect _quickConnect;
    private readonly StateManager _stateManager;
    private readonly ILogger<RemoteAuthController> _logger;

    public RemoteAuthController(
        UserSyncService userSyncService,
        ISessionManager sessionManager,
        IQuickConnect quickConnect,
        StateManager stateManager,
        ILogger<RemoteAuthController> logger)
    {
        _userSyncService = userSyncService;
        _sessionManager = sessionManager;
        _quickConnect = quickConnect;
        _stateManager = stateManager;
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
        var auth = TryAuthenticateHeaders();
        if (auth.Error != null)
        {
            return auth.Error;
        }

        var identity = auth.Identity!.Value;
        var username = identity.Username;
        var displayName = identity.DisplayName;
        var roles = identity.Roles;

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
            var basePath = GetBasePath();

            return Content(
                SessionHtml.BuildSuccessHtml(
                    authResult.AccessToken,
                    authResult.User.Id.ToString(),
                    authResult.ServerId,
                    basePath),
                "text/html");
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
    /// Trusted-header Quick Connect entry. Same secret + header validation as Login; returns
    /// an HTML form for entering the code shown by a native/TV app.
    /// </summary>
    [HttpGet("QuickConnect")]
    public async Task<ActionResult> QuickConnect()
    {
        if (!_quickConnect.IsEnabled)
        {
            return BadRequest("Quick Connect is not enabled on this server. An administrator can enable it under Dashboard > General.");
        }

        var auth = TryAuthenticateHeaders();
        if (auth.Error != null)
        {
            return auth.Error;
        }

        var identity = auth.Identity!.Value;
        var username = identity.Username;
        var displayName = identity.DisplayName;
        var roles = identity.Roles;

        try
        {
            var userId = await _userSyncService.SyncUserAsync(username, displayName, roles).ConfigureAwait(false);

            var sessionToken = _stateManager.CreateAuthorizedSession(new AuthorizedSession
            {
                Username = username,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName,
                Roles = roles,
                UserId = userId
            });

            return Content(SessionHtml.BuildQuickConnectHtml(sessionToken, GetBasePath()), "text/html");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("RemoteAuth: Quick Connect sync failed for {Username}: {Message}", username, ex.Message);
            return StatusCode(403, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RemoteAuth: Quick Connect failed for user {Username}", username);
            return StatusCode(500, "Authentication failed");
        }
    }

    /// <summary>
    /// Authorizes a pending Quick Connect request. Requires the same secret + identity headers as
    /// Login (proxy must inject them on this POST too), matching the QC session username.
    /// </summary>
    [HttpPost("QuickConnect/Authorize")]
    public async Task<ActionResult> QuickConnectAuthorize([FromBody] QuickConnectAuthorizeRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.Code))
        {
            return BadRequest("Missing session token or code");
        }

        // Secret + identity before peeking the session token (no unauthenticated capability URL).
        var auth = TryAuthenticateHeaders();
        if (auth.Error != null)
        {
            return auth.Error;
        }

        var identity = auth.Identity!.Value;

        if (!_quickConnect.IsEnabled)
        {
            return BadRequest("Quick Connect is not enabled on this server. An administrator can enable it under Dashboard > General.");
        }

        var session = _stateManager.PeekAuthorizedSession(request.Token);
        if (session == null)
        {
            return Unauthorized("Session expired. Please sign in again.");
        }

        if (!string.Equals(session.Username, identity.Username, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "RemoteAuth: Quick Connect identity mismatch session={SessionUser} header={HeaderUser}",
                session.Username,
                identity.Username);
            return Unauthorized("Identity mismatch");
        }

        var code = request.Code.Trim();

        try
        {
            var authorized = await _quickConnect.AuthorizeRequest(session.UserId, code).ConfigureAwait(false);
            if (!authorized)
            {
                return FailQuickConnectCode(request.Token, session, "Quick Connect authorization was rejected.");
            }
        }
        catch (Exception ex) when (ex.GetType().Name == "ResourceNotFoundException")
        {
            return FailQuickConnectCode(
                request.Token,
                session,
                "That code wasn't recognized. Check the code on your device and try again.");
        }
        catch (Exception ex) when (ex.GetType().Name == "AuthenticationException")
        {
            return BadRequest("Quick Connect is not active on this server.");
        }

        _stateManager.InvalidateAuthorizedSession(request.Token);
        _logger.LogInformation("RemoteAuth: Quick Connect authorized for user {Username}", session.Username);

        return Ok(new { success = true });
    }

    private ActionResult FailQuickConnectCode(string token, AuthorizedSession session, string message)
    {
        session.FailedCodeAttempts++;
        if (session.FailedCodeAttempts >= AuthorizedSession.MaxFailedCodeAttempts)
        {
            _stateManager.InvalidateAuthorizedSession(token);
            _logger.LogWarning(
                "RemoteAuth: Quick Connect session invalidated after {Attempts} failed codes for {Username}",
                session.FailedCodeAttempts,
                session.Username);
            return BadRequest("Too many invalid codes. Reload Quick Connect and try again.");
        }

        return BadRequest(message);
    }

    private string GetBasePath()
    {
        return Request.PathBase.HasValue ? Request.PathBase.Value!.TrimEnd('/') : "";
    }

    /// <summary>
    /// Shared secret + identity header validation for Login and QuickConnect.
    /// </summary>
    private (ActionResult? Error, (string Username, string DisplayName, string[] Roles)? Identity) TryAuthenticateHeaders()
    {
        var config = RemoteAuthPlugin.Instance?.Configuration;

        if (config == null || !config.Enabled)
        {
            return (StatusCode(503, "Remote Auth plugin is disabled"), null);
        }

        if (string.IsNullOrWhiteSpace(config.SecretHeaderValue))
        {
            _logger.LogWarning("RemoteAuth: SecretHeaderValue not configured — refusing all requests");
            return (StatusCode(503, "Remote Auth is not configured (missing secret)"), null);
        }

        var secretHeaderName = string.IsNullOrWhiteSpace(config.SecretHeaderName)
            ? "X-Remote-Auth-Secret"
            : config.SecretHeaderName;

        var incomingSecret = Request.Headers[secretHeaderName].ToString();

        if (!SecretComparer.Equals(incomingSecret, config.SecretHeaderValue))
        {
            _logger.LogWarning("RemoteAuth: invalid or missing secret header from {RemoteIp}",
                HttpContext.Connection.RemoteIpAddress);
            return (Unauthorized("Invalid or missing authentication secret"), null);
        }

        var userHeader = string.IsNullOrWhiteSpace(config.UserHeader) ? "X-Remote-Auth-User" : config.UserHeader;
        var username = Request.Headers[userHeader].ToString();

        if (string.IsNullOrWhiteSpace(username))
        {
            _logger.LogWarning("RemoteAuth: username header '{Header}' missing or empty", userHeader);
            return (Unauthorized("Username header is missing"), null);
        }

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

        return (null, (username, displayName, roles));
    }
}

public class QuickConnectAuthorizeRequest
{
    public string Token { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}
