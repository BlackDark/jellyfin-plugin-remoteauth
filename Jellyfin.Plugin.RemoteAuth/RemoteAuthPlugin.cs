using System;
using System.Collections.Generic;
using Jellyfin.Plugin.RemoteAuth.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.RemoteAuth;

public class RemoteAuthPlugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public RemoteAuthPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static RemoteAuthPlugin? Instance { get; private set; }

    public override string Name => "Remote Auth";

    public override Guid Id => Guid.Parse("a3c7e891-f2b4-4d6a-9e58-1b2c3d4e5f60");

    public override string Description => "Trusted header SSO with role-based library access control";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        var ns = GetType().Namespace;
        return new[]
        {
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = $"{ns}.Configuration.configPage.html"
            },
            new PluginPageInfo
            {
                Name = "remoteauthjs",
                EmbeddedResourcePath = $"{ns}.Configuration.remoteauth.js"
            }
        };
    }
}
