using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.RemoteAuth.Auth;

/// <summary>
/// Constant-time string comparison to prevent timing-based secret inference.
/// Note: the early length check does leak whether lengths match, but this is
/// unavoidable without HMAC. For a shared secret this is acceptable — the
/// important property is that same-length secrets cannot be brute-forced
/// character-by-character via timing.
/// </summary>
public static class SecretComparer
{
    public static bool Equals(string? a, string? b)
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
}
