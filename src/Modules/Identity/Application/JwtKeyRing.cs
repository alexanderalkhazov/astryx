using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Astryx.Modules.Identity.Application;

/// <summary>ES256 (ECDSA P-256) signing keys parsed from <see cref="JwtOptions.SigningKeys"/>.</summary>
public sealed class JwtKeyRing
{
    public JwtKeyRing(IOptions<JwtOptions> options)
    {
        var entries = options.Value.SigningKeys.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (entries.Length == 0)
        {
            throw new InvalidOperationException("Jwt:SigningKeys is not configured. Generate a key with `dotnet run --project apps/api -- --generate-jwt-key`.");
        }

        var keys = new List<ECDsaSecurityKey>();
        foreach (var entry in entries)
        {
            var separator = entry.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                throw new InvalidOperationException("Each Jwt:SigningKeys entry must look like 'kid:base64-pkcs8'.");
            }

            var ecdsa = ECDsa.Create();
            try
            {
                ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(entry[(separator + 1)..]), out _);
            }
            catch (Exception ex) when (ex is FormatException or CryptographicException)
            {
                ecdsa.Dispose();
                throw new InvalidOperationException($"Jwt:SigningKeys entry '{entry[..separator]}' is not a base64 PKCS#8 P-256 private key.", ex);
            }

            if (ecdsa.KeySize != 256)
            {
                throw new InvalidOperationException($"Jwt:SigningKeys entry '{entry[..separator]}' must be a P-256 key for ES256.");
            }

            keys.Add(new ECDsaSecurityKey(ecdsa) { KeyId = entry[..separator] });
        }

        ValidationKeys = keys;
        SigningCredentials = new SigningCredentials(keys[0], SecurityAlgorithms.EcdsaSha256);
    }

    public SigningCredentials SigningCredentials { get; }

    public IReadOnlyList<SecurityKey> ValidationKeys { get; }

    /// <summary>A fresh <c>kid:base64-pkcs8</c> entry for <see cref="JwtOptions.SigningKeys"/>.</summary>
    public static string Generate(string? keyId = null)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return $"{keyId ?? DateTime.UtcNow.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture)}:{Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey())}";
    }
}
