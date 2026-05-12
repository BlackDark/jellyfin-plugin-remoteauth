using System.Collections.Generic;
using Jellyfin.Plugin.RemoteAuth.Services;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.RemoteAuth.Api;

[ApiController]
[Route("sso/RemoteAuth/Config")]
[Authorize(Policy = Policies.RequiresElevation)]
public class ConfigController : ControllerBase
{
    private readonly RbacService _rbacService;

    public ConfigController(RbacService rbacService)
    {
        _rbacService = rbacService;
    }

    [HttpGet("Libraries")]
    public ActionResult<Dictionary<string, string>> GetLibraries()
    {
        return Ok(_rbacService.GetAvailableLibraries());
    }

    [HttpGet("Status")]
    public ActionResult GetStatus()
    {
        var config = RemoteAuthPlugin.Instance?.Configuration;
        return Ok(new
        {
            PluginVersion = RemoteAuthPlugin.Instance?.Version?.ToString() ?? "unknown",
            Enabled = config?.Enabled ?? false,
            Configured = !string.IsNullOrWhiteSpace(config?.SecretHeaderValue),
            RoleMappingCount = config?.RoleMappings.Count ?? 0,
            AutoCreateUsers = config?.AutoCreateUsers ?? false,
            DefaultRoleName = config?.DefaultRoleName ?? string.Empty
        });
    }
}
