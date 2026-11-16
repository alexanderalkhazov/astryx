using Astryx.Modules.Identity.Application;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Astryx.IntegrationTests;

/// <summary>
/// JWT signing-key rotation drill (SYSTEM_DESIGN.md §18.3, TASKS.md T-115): add the new key first (it signs), keep the
/// old one for validation until outstanding access tokens expire (15 min), then remove it.
/// </summary>
public class JwtKeyRotationTests
{
    private static readonly string OldKey = JwtKeyRing.Generate("2026-10");
    private static readonly string NewKey = JwtKeyRing.Generate("2026-11");

    private static JwtKeyRing Ring(string keys) => new(Options.Create(new JwtOptions { SigningKeys = keys }));

    private static string Issue(JwtKeyRing ring) =>
        new AccessTokenIssuer(ring, Options.Create(new JwtOptions()), TimeProvider.System).Issue(Guid.NewGuid(), Guid.NewGuid(), "stamp").Token;

    private static async Task<bool> ValidAsync(string token, JwtKeyRing ring)
    {
        var options = new JwtOptions();
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = options.Issuer,
            ValidAudience = options.Audience,
            IssuerSigningKeys = ring.ValidationKeys,
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            RequireSignedTokens = true,
        });
        return result.IsValid;
    }

    [Fact]
    public async Task Tokens_from_the_old_key_survive_the_overlap_and_stop_validating_once_it_is_removed()
    {
        var before = Issue(Ring(OldKey));

        var overlap = Ring($"{NewKey};{OldKey}");
        var after = Issue(overlap);

        (await ValidAsync(before, overlap)).ShouldBeTrue();
        (await ValidAsync(after, overlap)).ShouldBeTrue();
        new JsonWebToken(after).Kid.ShouldBe("2026-11"); // the first key signs

        var retired = Ring(NewKey);
        (await ValidAsync(after, retired)).ShouldBeTrue();
        (await ValidAsync(before, retired)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("nokid")]
    [InlineData("kid:not-base64!")]
    public void A_malformed_key_ring_refuses_to_start(string keys) =>
        Should.Throw<InvalidOperationException>(() => Ring(keys));
}
