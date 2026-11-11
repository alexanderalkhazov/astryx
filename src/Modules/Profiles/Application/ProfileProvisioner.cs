using Astryx.Modules.Profiles.Domain;
using Astryx.Modules.Profiles.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Astryx.Modules.Profiles.Application;

/// <summary>
/// Ensures a user's profile, risk settings and notification preferences exist. Used by the <c>UserRegistered</c>
/// handler and on first read, so a request that races account setup still sees defaults. Idempotent and race-safe.
/// </summary>
public sealed class ProfileProvisioner(ProfilesDbContext db, TimeProvider timeProvider)
{
    public async Task EnsureAsync(Guid userId, string? displayName, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (!await db.Profiles.AnyAsync(p => p.UserId == userId, cancellationToken))
        {
            db.Profiles.Add(new UserProfile { UserId = userId, DisplayName = displayName, CreatedAt = now, UpdatedAt = now });
        }

        if (!await db.RiskSettings.AnyAsync(r => r.UserId == userId, cancellationToken))
        {
            db.RiskSettings.Add(new RiskSettings { UserId = userId, UpdatedAt = now });
        }

        if (!await db.NotificationPreferences.AnyAsync(n => n.UserId == userId, cancellationToken))
        {
            db.NotificationPreferences.Add(new NotificationPreferences { UserId = userId, UpdatedAt = now });
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear(); // a concurrent caller created them first
        }
    }
}
