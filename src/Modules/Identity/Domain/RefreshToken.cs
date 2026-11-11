using Astryx.Infrastructure.Security;

namespace Astryx.Modules.Identity.Domain;

/// <summary>
/// Row in <c>identity.refresh_tokens</c> (SYSTEM_DESIGN.md §10.4, §18.3). Tokens are opaque, stored as SHA-256,
/// bound to one device, and chained in a family: each refresh revokes the presented token (<c>rotated</c>) and issues
/// its replacement in the same family.
/// </summary>
public sealed class RefreshToken : IUserOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public required byte[] TokenHash { get; set; }

    public Guid FamilyId { get; set; }

    /// <summary>When the family's first token was issued; caps the family's absolute lifetime.</summary>
    public DateTimeOffset FamilyStartedAt { get; set; }

    public required string DeviceId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public string? RevokedReason { get; set; }

    public Guid? ReplacedById { get; set; }

    public System.Net.IPAddress? CreatedIp { get; set; }

    public string? UserAgent { get; set; }
}

public static class RevokedReason
{
    public const string Rotated = "rotated";
    public const string Logout = "logout";
    public const string ReuseDetected = "reuse_detected";
    public const string PasswordChanged = "password_changed";
    public const string Superseded = "superseded";
    public const string Admin = "admin";
}
