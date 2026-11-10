namespace Astryx.Modules.Identity.Application;

/// <summary>Bound from configuration section <c>Identity</c>.</summary>
public sealed class AccountOptions
{
    /// <summary>PBKDF2-HMAC-SHA512 iterations (OWASP guidance ≥ 600,000; SYSTEM_DESIGN.md §18.2). Lower only in tests.</summary>
    public int PasswordHashIterations { get; set; } = 600_000;

    public TimeSpan EmailVerificationLifetime { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Base of the verification link opened by the app (custom scheme until a domain exists, Q-15).</summary>
    public string AppLinkBase { get; set; } = "astryx://";
}
