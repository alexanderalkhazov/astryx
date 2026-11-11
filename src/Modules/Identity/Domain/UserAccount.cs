using Astryx.Infrastructure.Security;

namespace Astryx.Modules.Identity.Domain;

/// <summary>Row in <c>identity.users</c> (SYSTEM_DESIGN.md §10.4).</summary>
public sealed class UserAccount
{
    public Guid Id { get; set; }

    public required string Email { get; set; }

    public required string NormalizedEmail { get; set; }

    public bool EmailConfirmed { get; set; }

    public required string PasswordHash { get; set; }

    public required string SecurityStamp { get; set; }

    public DateTimeOffset? LockoutEnd { get; set; }

    public int AccessFailedCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public static string Normalize(string email) => email.Trim().ToUpperInvariant();
}

/// <summary>Row in <c>identity.user_tokens</c>: a single-use, hashed email-verification or password-reset token.</summary>
public sealed class UserToken : IUserOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public required string Purpose { get; set; }

    public required byte[] TokenHash { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? ConsumedAt { get; set; }

    /// <summary>The outbox message that issued this token; makes the issuing handler idempotent.</summary>
    public Guid? SourceMessageId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public static class TokenPurpose
{
    public const string EmailVerification = "email_verification";
    public const string PasswordReset = "password_reset";
}
