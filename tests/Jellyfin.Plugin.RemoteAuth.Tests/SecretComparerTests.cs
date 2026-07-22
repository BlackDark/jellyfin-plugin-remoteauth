using Jellyfin.Plugin.RemoteAuth.Auth;

namespace Jellyfin.Plugin.RemoteAuth.Tests;

public class SecretComparerTests
{
    [Fact]
    public void Equals_SameSecrets_ReturnsTrue()
    {
        Assert.True(SecretComparer.Equals("shared-secret", "shared-secret"));
    }

    [Fact]
    public void Equals_DifferentSecretsSameLength_ReturnsFalse()
    {
        Assert.False(SecretComparer.Equals("shared-secret", "shared-secreX"));
    }

    [Fact]
    public void Equals_LengthMismatch_ReturnsFalse()
    {
        Assert.False(SecretComparer.Equals("short", "much-longer-secret"));
    }

    [Fact]
    public void Equals_NullInputs_ReturnsFalse()
    {
        Assert.False(SecretComparer.Equals(null, "secret"));
        Assert.False(SecretComparer.Equals("secret", null));
        Assert.False(SecretComparer.Equals(null, null));
    }
}
