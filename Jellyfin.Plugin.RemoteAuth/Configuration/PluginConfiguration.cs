using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.RemoteAuth.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public bool Enabled { get; set; } = true;

    // Secret header that the reverse proxy must send for every request
    public string SecretHeaderName { get; set; } = "X-Remote-Auth-Secret";

    public string SecretHeaderValue { get; set; } = string.Empty;

    // Identity headers injected by the reverse proxy after successful auth
    public string UserHeader { get; set; } = "X-Remote-Auth-User";

    public string EmailHeader { get; set; } = "X-Remote-Auth-Email";

    public string DisplayNameHeader { get; set; } = "X-Remote-Auth-Name";

    public string GroupsHeader { get; set; } = "X-Remote-Auth-Groups";

    // Delimiter used when the proxy sends multiple groups in one header value
    public string GroupsDelimiter { get; set; } = "|";

    // If set, users whose groups include this value are granted admin in Jellyfin
    // regardless of role mappings (convenience shortcut)
    public string AdminGroup { get; set; } = string.Empty;

    public bool AutoCreateUsers { get; set; } = true;

    public string DefaultRoleName { get; set; } = string.Empty;

    public List<RoleMapping> RoleMappings { get; set; } = new();
}

public class RoleMapping
{
    public string RoleName { get; set; } = string.Empty;

    public bool IsAdmin { get; set; }

    public bool EnableAllLibraries { get; set; }

    public List<string> LibraryIds { get; set; } = new();

    public List<string> LibraryNames { get; set; } = new();

    public bool EnableLiveTv { get; set; }

    public bool EnableLiveTvManagement { get; set; }

    public bool EnableMediaPlayback { get; set; } = true;

    public bool EnableRemoteAccess { get; set; } = true;

    public bool EnableTranscoding { get; set; } = true;

    public bool EnableContentDeletion { get; set; }

    public bool EnableCollectionManagement { get; set; }

    public bool EnableSubtitleManagement { get; set; }

    public int? MaxParentalRating { get; set; }

    public int Priority { get; set; }
}
