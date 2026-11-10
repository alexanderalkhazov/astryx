using System.Security.Cryptography;

namespace Astryx.Modules.Identity.Domain;

/// <summary>256-bit random tokens, shown once (base64url) and stored only as a SHA-256 hash (SYSTEM_DESIGN.md §18.1).</summary>
public static class SecureToken
{
    public static (string Plain, byte[] Hash) Create()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var plain = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (plain, Hash(plain));
    }

    public static byte[] Hash(string plain) => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(plain));

    public static string NewSecurityStamp() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
}
