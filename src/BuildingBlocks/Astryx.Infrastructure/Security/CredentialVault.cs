using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Astryx.Infrastructure.Security;

/// <summary>
/// <c>Credentials:MasterKeys</c>: <c>id:base64key;id:base64key</c>, newest first (SYSTEM_DESIGN.md §17.6, §18.12). Each key is
/// 32 random bytes. The first key wraps new secrets; older keys only unwrap until every record is re-encrypted. In
/// production the master key moves to a KMS (T-134).
/// </summary>
public sealed class CredentialVaultOptions
{
    public const string SectionName = "Credentials";

    public string MasterKeys { get; set; } = string.Empty;
}

/// <summary>An encrypted secret as stored: the opaque envelope and the id of the master key that wrapped it.</summary>
public sealed record SealedSecret(byte[] Ciphertext, string KeyId);

/// <summary>
/// Envelope encryption for broker credentials (SYSTEM_DESIGN.md §17.6): every secret gets a fresh AES-256-GCM data
/// key, and the data key is itself encrypted with the current master key. The envelope is
/// <c>version(1) | wrapNonce(12) | wrapTag(16) | wrappedKey(32) | nonce(12) | tag(16) | ciphertext</c>, and the record
/// id is authenticated as associated data, so a ciphertext copied onto another row fails to open.
/// </summary>
public sealed class CredentialVault
{
    private const byte Version = 1;
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly IReadOnlyList<(string Id, byte[] Key)> _keys;

    public CredentialVault(IOptions<CredentialVaultOptions> options)
    {
        _keys = Parse(options.Value.MasterKeys);
    }

    /// <summary>True when a master key is configured; connecting a broker is refused otherwise.</summary>
    public bool IsConfigured => _keys.Count > 0;

    public SealedSecret Seal(string plaintext, Guid recordId)
    {
        var (keyId, master) = _keys.Count > 0 ? _keys[0] : throw new InvalidOperationException("Credentials:MasterKeys is not configured.");
        var dataKey = RandomNumberGenerator.GetBytes(KeySize);
        try
        {
            var aad = recordId.ToByteArray();
            var envelope = new byte[1 + NonceSize + TagSize + KeySize + NonceSize + TagSize + Encoding.UTF8.GetByteCount(plaintext)];
            envelope[0] = Version;
            var span = envelope.AsSpan(1);
            var wrapNonce = span[..NonceSize];
            var wrapTag = span.Slice(NonceSize, TagSize);
            var wrapped = span.Slice(NonceSize + TagSize, KeySize);
            var nonce = span.Slice(NonceSize + TagSize + KeySize, NonceSize);
            var tag = span.Slice((2 * NonceSize) + TagSize + KeySize, TagSize);
            var ciphertext = span[((2 * NonceSize) + (2 * TagSize) + KeySize)..];

            RandomNumberGenerator.Fill(wrapNonce);
            using (var wrap = new AesGcm(master, TagSize))
            {
                wrap.Encrypt(wrapNonce, dataKey, wrapped, wrapTag, aad);
            }

            RandomNumberGenerator.Fill(nonce);
            using (var aes = new AesGcm(dataKey, TagSize))
            {
                aes.Encrypt(nonce, Encoding.UTF8.GetBytes(plaintext), ciphertext, tag, aad);
            }

            return new SealedSecret(envelope, keyId);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    /// <exception cref="CryptographicException">Unknown key id, a tampered envelope, or another record's ciphertext.</exception>
    public string Open(SealedSecret secret, Guid recordId)
    {
        var master = _keys.FirstOrDefault(k => k.Id == secret.KeyId).Key ?? throw new CryptographicException($"Master key '{secret.KeyId}' is not configured.");
        var envelope = secret.Ciphertext.AsSpan();
        if (envelope.Length < 1 + (2 * NonceSize) + (2 * TagSize) + KeySize || envelope[0] != Version)
        {
            throw new CryptographicException("Unrecognised credential envelope.");
        }

        var aad = recordId.ToByteArray();
        var span = envelope[1..];
        var dataKey = new byte[KeySize];
        try
        {
            using (var wrap = new AesGcm(master, TagSize))
            {
                wrap.Decrypt(span[..NonceSize], span.Slice(NonceSize + TagSize, KeySize), span.Slice(NonceSize, TagSize), dataKey, aad);
            }

            var ciphertext = span[((2 * NonceSize) + (2 * TagSize) + KeySize)..];
            var plaintext = new byte[ciphertext.Length];
            using (var aes = new AesGcm(dataKey, TagSize))
            {
                aes.Decrypt(span.Slice(NonceSize + TagSize + KeySize, NonceSize), ciphertext, span.Slice((2 * NonceSize) + TagSize + KeySize, TagSize), plaintext, aad);
            }

            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    /// <summary>A new ring entry for <c>Credentials:MasterKeys</c> (<c>--generate-credentials-key</c>).</summary>
    public static string GenerateKey() => $"k{DateTimeOffset.UtcNow:yyyyMMdd}:{Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeySize))}";

    private static List<(string Id, byte[] Key)> Parse(string ring)
    {
        var keys = new List<(string, byte[])>();
        foreach (var entry in ring.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = entry.IndexOf(':', StringComparison.Ordinal);
            var key = colon > 0 ? Convert.FromBase64String(entry[(colon + 1)..]) : throw new InvalidOperationException("Credentials:MasterKeys entries are 'id:base64key'.");
            if (key.Length != KeySize)
            {
                throw new InvalidOperationException($"Credentials master key '{entry[..colon]}' must be {KeySize} bytes.");
            }

            keys.Add((entry[..colon], key));
        }

        return keys;
    }
}
