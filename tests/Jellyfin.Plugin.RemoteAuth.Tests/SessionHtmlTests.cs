using Jellyfin.Plugin.RemoteAuth.Api;

namespace Jellyfin.Plugin.RemoteAuth.Tests;

public class SessionHtmlTests
{
    [Fact]
    public void BuildSuccessHtml_EmptyBasePath_UsesOriginAndRootRedirect()
    {
        var html = SessionHtml.BuildSuccessHtml("tok", "uid", "sid", "");

        Assert.Contains("ManualAddress: window.location.origin + \"\"", html);
        Assert.Contains("window.location.href = \"/\"", html);
        Assert.Contains("\"tok\"", html);
        Assert.Contains("\"uid\"", html);
        Assert.Contains("\"sid\"", html);
    }

    [Fact]
    public void BuildSuccessHtml_WithBasePath_IncludesPrefixInAddressAndRedirect()
    {
        var html = SessionHtml.BuildSuccessHtml("tok", "uid", "sid", "/jellyfin");

        Assert.Contains("ManualAddress: window.location.origin + \"/jellyfin\"", html);
        Assert.Contains("window.location.href = \"/jellyfin/\"", html);
    }

    [Fact]
    public void BuildQuickConnectHtml_PostsToRemoteAuthAuthorizeWithBasePath()
    {
        var html = SessionHtml.BuildQuickConnectHtml("session-token", "/jellyfin");

        Assert.Contains("const token = \"session-token\";", html);
        Assert.Contains("fetch(basePath + '/sso/RemoteAuth/QuickConnect/Authorize'", html);
        Assert.Contains("const basePath = \"/jellyfin\";", html);
        Assert.DoesNotContain("providerId", html);
    }

    [Fact]
    public void BuildQuickConnectHtml_EmptyBasePath_UsesEmptyString()
    {
        var html = SessionHtml.BuildQuickConnectHtml("t", "");

        Assert.Contains("const basePath = \"\";", html);
        Assert.Contains("/sso/RemoteAuth/QuickConnect/Authorize", html);
    }
}
