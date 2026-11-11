using Astryx.Contracts.Audit;
using Astryx.Modules.Identity.Domain;
using Astryx.Modules.Identity.Infrastructure.Persistence;
using Astryx.SharedKernel.Results;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Identity.Application;

/// <summary>Forgot / reset / change password (SYSTEM_DESIGN.md §11.3 A8, A9, A12; §18.4).</summary>
public sealed class PasswordService(IdentityDbContext db, IPasswordHasher<UserAccount> hasher, SecurityStampCache stamps, TimeProvider timeProvider)
{
    public static readonly Error InvalidOrExpiredToken = new("invalid_or_expired_token", "This reset link is invalid or has expired. Request a new one.");
    public static readonly Error CurrentPasswordIncorrect = new("current_password_incorrect", "Your current password is incorrect.");
    public static readonly Error SamePassword = new("validation_failed", "Choose a password different from your current one.");

    /// <summary>Queues a reset email when the account exists; the caller always gets the same response.</summary>
    public async Task RequestResetAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = UserAccount.Normalize(email);
        var userId = await db.Users.AsNoTracking()
            .Where(u => u.NormalizedEmail == normalized && u.DeletedAt == null)
            .Select(u => (Guid?)u.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (userId is { } id)
        {
            db.Publish(new PasswordResetRequested(id));
            db.Audit(new AuditEntry("auth.password_reset_requested", AuditActor.User, id, "user", id.ToString()));
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Sets the new password, ends every session, clears any lockout and confirms the email (the link proved ownership).
    /// </summary>
    public async Task<Result> ResetAsync(Guid userId, string token, string newPassword, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var hash = SecureToken.Hash(token);
        var resetToken = await db.UserTokens.SingleOrDefaultAsync(
            t => t.TokenHash == hash && t.UserId == userId && t.Purpose == TokenPurpose.PasswordReset, cancellationToken);
        if (resetToken is null || resetToken.ConsumedAt is not null || resetToken.ExpiresAt <= now)
        {
            return InvalidOrExpiredToken;
        }

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null, cancellationToken);
        if (user is null)
        {
            return InvalidOrExpiredToken;
        }

        resetToken.ConsumedAt = now;
        user.PasswordHash = hasher.HashPassword(user, newPassword);
        user.SecurityStamp = SecureToken.NewSecurityStamp();
        user.EmailConfirmed = true;
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        user.UpdatedAt = now;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await RevokeSessionsAsync(userId, keepFamilyId: null, now, cancellationToken);
        db.Publish(new PasswordChanged(userId, "reset"));
        db.Audit(new AuditEntry("auth.password_reset", AuditActor.User, userId, "user", userId.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        stamps.Invalidate(userId);
        return Result.Success();
    }

    /// <summary>
    /// Changes the password of a signed-in user. Other sessions end; the current one (<paramref name="currentFamilyId"/>)
    /// stays, and its next refresh yields an access token carrying the new security stamp.
    /// </summary>
    public async Task<Result> ChangeAsync(Guid userId, Guid currentFamilyId, string currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var user = await db.Users.SingleAsync(u => u.Id == userId, cancellationToken);
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
        {
            return CurrentPasswordIncorrect;
        }

        if (hasher.VerifyHashedPassword(user, user.PasswordHash, newPassword) != PasswordVerificationResult.Failed)
        {
            return SamePassword;
        }

        user.PasswordHash = hasher.HashPassword(user, newPassword);
        user.SecurityStamp = SecureToken.NewSecurityStamp();
        user.UpdatedAt = now;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await RevokeSessionsAsync(userId, currentFamilyId, now, cancellationToken);
        db.Publish(new PasswordChanged(userId, "change"));
        db.Audit(new AuditEntry("auth.password_changed", AuditActor.User, userId, "user", userId.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        stamps.Invalidate(userId);
        return Result.Success();
    }

    private Task<int> RevokeSessionsAsync(Guid userId, Guid? keepFamilyId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && (keepFamilyId == null || t.FamilyId != keepFamilyId))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokedReason, RevokedReason.PasswordChanged), cancellationToken);
}
