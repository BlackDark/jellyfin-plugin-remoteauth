namespace Jellyfin.Plugin.RemoteAuth.Auth;

/// <summary>
/// Stable AuthenticationProviderId strings used by Jellyfin / this plugin.
/// </summary>
internal static class AuthenticationProviderIds
{
    /// <summary>
    /// Jellyfin's built-in username/password provider (FullName of DefaultAuthenticationProvider).
    /// </summary>
    public const string Default =
        "Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider";

    public static string RemoteAuth => typeof(RemoteAuthProvider).FullName!;
}
