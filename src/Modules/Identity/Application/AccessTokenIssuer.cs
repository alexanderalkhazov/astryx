using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Astryx.Modules.Identity.Application;

public static class AstryxClaims
{
    public const string Subject = "sub";
    public const string Session = "sid";
    public const string SecurityStamp = "sstamp";
}

/// <summary>Issues 15-minute ES256 access tokens (SYSTEM_DESIGN.md §18.3).</summary>
public sealed class AccessTokenIssuer(JwtKeyRing keys, IOptions<JwtOptions> options, TimeProvider timeProvider)
{
    private static readonly JsonWebTokenHandler Handler = new();

    public (string Token, DateTimeOffset ExpiresAt) Issue(Guid userId, Guid familyId, string securityStamp)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();
        var expires = now.AddMinutes(settings.AccessTokenMinutes);
        var token = Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = keys.SigningCredentials,
            Subject = new ClaimsIdentity(
            [
                new Claim(AstryxClaims.Subject, userId.ToString()),
                new Claim(AstryxClaims.Session, familyId.ToString()),
                new Claim(AstryxClaims.SecurityStamp, StampFingerprint(securityStamp)),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            ]),
        });
        return (token, expires);
    }

    /// <summary>The token carries a fingerprint of the stamp, not the stamp itself.</summary>
    public static string StampFingerprint(string securityStamp) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(securityStamp)))[..16];
}
