using Astryx.Contracts.Audit;
using Astryx.Contracts.Events.Identity;
using Astryx.Modules.Identity.Domain;
using Astryx.Modules.Identity.Infrastructure.Persistence;
using Astryx.SharedKernel;
using Astryx.SharedKernel.Results;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Astryx.Modules.Identity.Application;

public sealed record RegisterCommand(string Email, string Password, string? DisplayName);

public sealed record VerifyEmailCommand(Guid UserId, string Token);

/// <summary>Registration, email verification and resend (SYSTEM_DESIGN.md §7.1, §11.3 A1–A3, §18.1).</summary>
public sealed class RegistrationService(IdentityDbContext db, IPasswordHasher<UserAccount> hasher, TimeProvider timeProvider)
{
    public static readonly Error InvalidOrExpiredToken = new("invalid_or_expired_token", "This verification link is invalid or has expired. Request a new one.");

    /// <summary>
    /// Always succeeds from the caller's point of view, so the response never reveals whether an email is registered.
    /// The password is hashed in both branches to keep timing similar.
    /// </summary>
    public async Task RegisterAsync(RegisterCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var email = command.Email.Trim();
        var normalized = UserAccount.Normalize(email);
        var candidate = new UserAccount
        {
            Id = Ids.New(timeProvider),
            Email = email,
            NormalizedEmail = normalized,
            PasswordHash = string.Empty,
            SecurityStamp = SecureToken.NewSecurityStamp(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        candidate.PasswordHash = hasher.HashPassword(candidate, command.Password);

        var existing = await db.Users.AsNoTracking().Where(u => u.NormalizedEmail == normalized).Select(u => (Guid?)u.Id).SingleOrDefaultAsync(cancellationToken);
        if (existing is { } existingId)
        {
            await NotifyExistingAccountAsync(existingId, cancellationToken);
            return;
        }

        db.Users.Add(candidate);
        db.Publish(new UserRegistered(candidate.Id, string.IsNullOrWhiteSpace(command.DisplayName) ? null : command.DisplayName.Trim()));
        db.Publish(new EmailVerificationRequested(candidate.Id));
        db.Audit(new AuditEntry("auth.registered", AuditActor.User, candidate.Id, "user", candidate.Id.ToString(), After: new { candidate.Email }));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Lost a race with a concurrent registration of the same email: behave exactly like the existing-account path.
            db.ChangeTracker.Clear();
            var winner = await db.Users.AsNoTracking().Where(u => u.NormalizedEmail == normalized).Select(u => u.Id).SingleAsync(cancellationToken);
            await NotifyExistingAccountAsync(winner, cancellationToken);
        }
    }

    public async Task<Result> VerifyEmailAsync(VerifyEmailCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var hash = SecureToken.Hash(command.Token);
        var token = await db.UserTokens.SingleOrDefaultAsync(
            t => t.TokenHash == hash && t.UserId == command.UserId && t.Purpose == TokenPurpose.EmailVerification,
            cancellationToken);
        if (token is null || token.ConsumedAt is not null || token.ExpiresAt <= now)
        {
            return InvalidOrExpiredToken;
        }

        var user = await db.Users.SingleAsync(u => u.Id == command.UserId, cancellationToken);
        token.ConsumedAt = now;
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            user.UpdatedAt = now;
            db.Audit(new AuditEntry("auth.email_verified", AuditActor.User, user.Id, "user", user.Id.ToString()));
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Queues a new verification email for an unverified account; silently does nothing otherwise.</summary>
    public async Task ResendVerificationAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = UserAccount.Normalize(email);
        var user = await db.Users.AsNoTracking()
            .Where(u => u.NormalizedEmail == normalized && !u.EmailConfirmed && u.DeletedAt == null)
            .Select(u => (Guid?)u.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (user is { } userId)
        {
            db.Publish(new EmailVerificationRequested(userId));
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task NotifyExistingAccountAsync(Guid userId, CancellationToken cancellationToken)
    {
        db.Publish(new ExistingAccountRegistrationAttempted(userId));
        db.Audit(new AuditEntry("auth.registration_attempt_existing", AuditActor.User, userId, "user", userId.ToString()));
        await db.SaveChangesAsync(cancellationToken);
    }
}
