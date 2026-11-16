using Astryx.Contracts.Audit;
using Astryx.Infrastructure.Audit;
using Astryx.Infrastructure.Observability;
using Astryx.Modules.Identity.Domain;
using Astryx.Modules.Identity.Infrastructure.Persistence;
using Astryx.SharedKernel;
using Astryx.SharedKernel.Results;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Astryx.Modules.Identity.Application;

public sealed record TokenPair(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);

/// <summary>Login, refresh rotation with reuse detection, logout (SYSTEM_DESIGN.md §6.B, §11.3 A4–A7, §18.3).</summary>
public sealed class SessionService(
    IdentityDbContext db,
    IPasswordHasher<UserAccount> hasher,
    AccessTokenIssuer accessTokens,
    SecurityStampCache stamps,
    IOptions<JwtOptions> jwtOptions,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    /// <summary>One response for wrong password, unknown email and locked account, so none of them can be told apart.</summary>
    public static readonly Error InvalidCredentials = new("invalid_credentials", "Email or password is incorrect, or sign-in is temporarily locked after repeated attempts.");
    public static readonly Error EmailNotVerified = new("email_not_verified", "Verify your email before signing in. Check your inbox for the link, or request a new one.");
    public static readonly Error SessionRevoked = new("session_revoked", "Your session has ended. Sign in again.");
    public static readonly Error RefreshSuperseded = new("refresh_token_superseded", "This refresh token was just replaced. Use the newest token.");

    // A real Identity v3 hash, verified when the email is unknown so both paths cost one PBKDF2 run.
    private static readonly Lazy<string> DummyHash = new(() => new PasswordHasher<UserAccount>().HashPassword(null!, "timing-equaliser-password"));

    public async Task<Result<TokenPair>> LoginAsync(string email, string password, string deviceId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var normalized = UserAccount.Normalize(email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == normalized && u.DeletedAt == null, cancellationToken);
        if (user is null)
        {
            hasher.VerifyHashedPassword(null!, DummyHash.Value, password);
            await auditLog.RecordAsync(new AuditEntry("auth.login_failed", AuditActor.User, Metadata: new { email, reason = "unknown_email" }), cancellationToken);
            AstryxTelemetry.AuthLoginFailures.Add(1, new KeyValuePair<string, object?>("reason", "unknown_email"));
            return InvalidCredentials;
        }

        if (user.LockoutEnd > now)
        {
            hasher.VerifyHashedPassword(user, user.PasswordHash, password);
            await auditLog.RecordAsync(new AuditEntry("auth.login_failed", AuditActor.User, user.Id, "user", user.Id.ToString(), Metadata: new { reason = "locked" }), cancellationToken);
            AstryxTelemetry.AuthLoginFailures.Add(1, new KeyValuePair<string, object?>("reason", "locked"));
            return InvalidCredentials;
        }

        var verification = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            user.AccessFailedCount++;
            var locked = user.AccessFailedCount >= MaxFailedAttempts;
            if (locked)
            {
                user.LockoutEnd = now + LockoutDuration;
                user.AccessFailedCount = 0;
            }

            user.UpdatedAt = now;
            db.Audit(new AuditEntry(locked ? "auth.locked_out" : "auth.login_failed", AuditActor.User, user.Id, "user", user.Id.ToString(), Metadata: new { reason = "wrong_password" }));
            AstryxTelemetry.AuthLoginFailures.Add(1, new KeyValuePair<string, object?>("reason", "wrong_password"));
            await db.SaveChangesAsync(cancellationToken);
            return InvalidCredentials;
        }

        if (!user.EmailConfirmed)
        {
            return EmailNotVerified;
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, password);
        }

        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        user.UpdatedAt = now;

        // Signing in again on the same device replaces that device's previous session.
        await db.RefreshTokens
            .Where(t => t.UserId == user.Id && t.DeviceId == deviceId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokedReason, RevokedReason.Superseded), cancellationToken);

        var familyId = Ids.New(timeProvider);
        var (plain, refresh) = NewRefreshToken(user.Id, familyId, now, deviceId, now);
        db.RefreshTokens.Add(refresh);
        db.Audit(new AuditEntry("auth.login", AuditActor.User, user.Id, "session", familyId.ToString(), Metadata: new { deviceId }));
        await db.SaveChangesAsync(cancellationToken);

        var (access, accessExpires) = accessTokens.Issue(user.Id, familyId, user.SecurityStamp);
        return new TokenPair(access, accessExpires, plain, refresh.ExpiresAt);
    }

    public async Task<Result<TokenPair>> RefreshAsync(string refreshToken, string deviceId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var settings = jwtOptions.Value;
        var hash = SecureToken.Hash(refreshToken);
        var current = await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (current is null)
        {
            return SessionRevoked;
        }

        if (current.DeviceId != deviceId)
        {
            // A token presented from another device is treated as stolen.
            await RevokeFamilyForReuseAsync(current, now, "device_mismatch", cancellationToken);
            return SessionRevoked;
        }

        if (current.RevokedAt is not null)
        {
            var withinGrace = current.RevokedReason == RevokedReason.Rotated && now - current.RevokedAt.Value <= TimeSpan.FromSeconds(settings.RefreshReuseGraceSeconds);
            if (withinGrace)
            {
                return RefreshSuperseded;
            }

            if (current.RevokedReason is RevokedReason.Rotated)
            {
                await RevokeFamilyForReuseAsync(current, now, "rotated_token_reused", cancellationToken);
            }

            return SessionRevoked;
        }

        if (current.ExpiresAt <= now || current.FamilyStartedAt.AddDays(settings.RefreshFamilyMaxDays) <= now)
        {
            return SessionRevoked;
        }

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == current.UserId && u.DeletedAt == null && u.EmailConfirmed, cancellationToken);
        if (user is null)
        {
            return SessionRevoked;
        }

        var (plain, next) = NewRefreshToken(user.Id, current.FamilyId, current.FamilyStartedAt, deviceId, now);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.RefreshTokens.Add(next);
        await db.SaveChangesAsync(cancellationToken);

        // Only one concurrent refresh of the same token can win this conditional update.
        var rotated = await db.RefreshTokens
            .Where(t => t.Id == current.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokedReason, RevokedReason.Rotated).SetProperty(t => t.ReplacedById, next.Id), cancellationToken);
        if (rotated == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return RefreshSuperseded;
        }

        await transaction.CommitAsync(cancellationToken);
        var (access, accessExpires) = accessTokens.Issue(user.Id, current.FamilyId, user.SecurityStamp);
        return new TokenPair(access, accessExpires, plain, next.ExpiresAt);
    }

    /// <summary>Ends the session the refresh token belongs to. Idempotent; unknown or foreign tokens are ignored.</summary>
    public async Task LogoutAsync(Guid userId, string refreshToken, CancellationToken cancellationToken)
    {
        var hash = SecureToken.Hash(refreshToken);
        var familyId = await db.RefreshTokens.AsNoTracking().Where(t => t.TokenHash == hash && t.UserId == userId).Select(t => (Guid?)t.FamilyId).SingleOrDefaultAsync(cancellationToken);
        if (familyId is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        await db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokedReason, RevokedReason.Logout), cancellationToken);
        await auditLog.RecordAsync(new AuditEntry("auth.logout", AuditActor.User, userId, "session", familyId.ToString()), cancellationToken);
    }

    /// <summary>Revokes every session and rotates the security stamp, which also invalidates outstanding access tokens.</summary>
    public async Task LogoutAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var user = await db.Users.SingleAsync(u => u.Id == userId, cancellationToken);
        user.SecurityStamp = SecureToken.NewSecurityStamp();
        user.UpdatedAt = now;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokedReason, RevokedReason.Logout), cancellationToken);
        db.Audit(new AuditEntry("auth.logout_all", AuditActor.User, userId, "user", userId.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        stamps.Invalidate(userId);
    }

    private async Task RevokeFamilyForReuseAsync(RefreshToken token, DateTimeOffset now, string reason, CancellationToken cancellationToken)
    {
        AstryxTelemetry.AuthRefreshReuse.Add(1, new KeyValuePair<string, object?>("reason", reason));
        await db.RefreshTokens
            .Where(t => t.FamilyId == token.FamilyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokedReason, RevokedReason.ReuseDetected), cancellationToken);
        await auditLog.RecordAsync(new AuditEntry("auth.refresh_reuse_detected", AuditActor.User, token.UserId, "session", token.FamilyId.ToString(), Metadata: new { reason, deviceId = token.DeviceId }), cancellationToken);
    }

    private (string Plain, RefreshToken Entity) NewRefreshToken(Guid userId, Guid familyId, DateTimeOffset familyStartedAt, string deviceId, DateTimeOffset now)
    {
        var settings = jwtOptions.Value;
        var (plain, hash) = SecureToken.Create();
        var sliding = now.AddDays(settings.RefreshTokenDays);
        var absolute = familyStartedAt.AddDays(settings.RefreshFamilyMaxDays);
        return (plain, new RefreshToken
        {
            Id = Ids.New(timeProvider),
            UserId = userId,
            TokenHash = hash,
            FamilyId = familyId,
            FamilyStartedAt = familyStartedAt,
            DeviceId = deviceId,
            CreatedAt = now,
            ExpiresAt = sliding < absolute ? sliding : absolute,
            CreatedIp = System.Net.IPAddress.TryParse(ClientContext.Ip, out var ip) ? ip : null,
            UserAgent = ClientContext.UserAgent,
        });
    }
}
