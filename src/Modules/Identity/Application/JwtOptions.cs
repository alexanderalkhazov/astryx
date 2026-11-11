namespace Astryx.Modules.Identity.Application;

/// <summary>Bound from configuration section <c>Jwt</c> (SYSTEM_DESIGN.md §18.3).</summary>
public sealed class JwtOptions
{
    public string Issuer { get; set; } = "astryx-api";

    public string Audience { get; set; } = "astryx-mobile";

    public int AccessTokenMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 30;

    /// <summary>A refresh family can be extended by rotation for at most this long before the user signs in again.</summary>
    public int RefreshFamilyMaxDays { get; set; } = 90;

    /// <summary>A rotated token presented again within this window is treated as a client race, not theft.</summary>
    public int RefreshReuseGraceSeconds { get; set; } = 10;

    /// <summary>
    /// <c>kid:base64-pkcs8-p256;kid2:…</c>. The first key signs; all keys verify (rotation). Generate one with
    /// <c>dotnet run --project apps/api -- --generate-jwt-key</c>. Keep it in a secret store, never in appsettings.
    /// </summary>
    public string SigningKeys { get; set; } = string.Empty;
}
