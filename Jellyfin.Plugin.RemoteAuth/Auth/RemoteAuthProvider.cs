using System;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Authentication;

namespace Jellyfin.Plugin.RemoteAuth.Auth;

/// <summary>
/// Authentication provider assigned to Remote Auth-managed users.
/// Blocks direct username/password login — users must authenticate via the reverse proxy.
/// </summary>
public class RemoteAuthProvider : IAuthenticationProvider
{
    public string Name => "Remote Auth";

    public bool IsEnabled => true;

    public Task<ProviderAuthenticationResult> Authenticate(string username, string password)
    {
        throw new AuthenticationException(
            "This account uses Remote Auth (trusted header SSO). Password login is disabled.");
    }

    public bool HasPassword(User user)
    {
        return false;
    }

    public Task ChangePassword(User user, string newPassword)
    {
        throw new NotSupportedException("Password management is handled by the identity provider.");
    }
}
