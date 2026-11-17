using System.Security.Cryptography;
using System.Text;
using Astryx.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Astryx.UnitTests.Security;

/// <summary>Envelope encryption of broker credentials (SYSTEM_DESIGN.md §17.6, TASKS.md T-141).</summary>
public class CredentialVaultTests
{
    private static readonly string Current = "new:" + CredentialVault.GenerateKey().Split(':', 2)[1];
    private static readonly string Previous = "old:" + CredentialVault.GenerateKey().Split(':', 2)[1];

    private static CredentialVault Vault(string ring) => new(Options.Create(new CredentialVaultOptions { MasterKeys = ring }));

    [Fact]
    public void A_sealed_secret_opens_only_for_its_record_and_never_contains_the_plaintext()
    {
        var vault = Vault(Current);
        var record = Guid.NewGuid();

        var sealedSecret = vault.Seal("""{"keyId":"PK123","secretKey":"s3cret-value"}""", record);

        Encoding.UTF8.GetString(sealedSecret.Ciphertext).ShouldNotContain("s3cret-value");
        sealedSecret.KeyId.ShouldBe(Current.Split(':')[0]);
        vault.Open(sealedSecret, record).ShouldContain("s3cret-value");
        Should.Throw<CryptographicException>(() => vault.Open(sealedSecret, Guid.NewGuid())); // copied onto another row
    }

    [Fact]
    public void Each_seal_uses_a_fresh_data_key()
    {
        var vault = Vault(Current);
        var record = Guid.NewGuid();

        vault.Seal("same", record).Ciphertext.ShouldNotBe(vault.Seal("same", record).Ciphertext);
    }

    [Fact]
    public void A_tampered_envelope_fails_to_open()
    {
        var vault = Vault(Current);
        var record = Guid.NewGuid();
        var sealedSecret = vault.Seal("secret", record);
        sealedSecret.Ciphertext[^1] ^= 0x01;

        Should.Throw<CryptographicException>(() => vault.Open(sealedSecret, record));
    }

    [Fact]
    public void An_older_master_key_still_opens_what_it_sealed_after_rotation()
    {
        var record = Guid.NewGuid();
        var sealedByOld = Vault(Previous).Seal("secret", record);

        var rotated = Vault($"{Current};{Previous}");

        rotated.Open(sealedByOld, record).ShouldBe("secret");
        rotated.Seal("secret", record).KeyId.ShouldBe(Current.Split(':')[0]); // new secrets use the newest key
        Should.Throw<CryptographicException>(() => Vault(Current).Open(sealedByOld, record)); // the old key was removed
    }

    [Fact]
    public void Without_a_master_key_the_vault_refuses_to_seal()
    {
        var vault = Vault(string.Empty);

        vault.IsConfigured.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => vault.Seal("secret", Guid.NewGuid()));
        Should.Throw<InvalidOperationException>(() => Vault("bad:" + Convert.ToBase64String(new byte[16])));
    }
}
