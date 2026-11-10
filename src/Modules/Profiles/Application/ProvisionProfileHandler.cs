using Astryx.Contracts.Events;
using Astryx.Contracts.Events.Identity;
using Astryx.Modules.Profiles.Domain;
using Astryx.Modules.Profiles.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Profiles.Application;

/// <summary>Creates the profile, default risk limits and notification preferences for a new user. Idempotent.</summary>
public sealed class ProvisionProfileHandler(ProfilesDbContext db, TimeProvider timeProvider) : IIntegrationEventHandler<UserRegistered>
{
    public async Task HandleAsync(UserRegistered integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken)
    {
        var userId = integrationEvent.UserId;
        var now = timeProvider.GetUtcNow();
        if (!await db.Profiles.AnyAsync(p => p.UserId == userId, cancellationToken))
        {
            db.Profiles.Add(new UserProfile { UserId = userId, DisplayName = integrationEvent.DisplayName, CreatedAt = now, UpdatedAt = now });
        }

        if (!await db.RiskSettings.AnyAsync(r => r.UserId == userId, cancellationToken))
        {
            db.RiskSettings.Add(new RiskSettings { UserId = userId, UpdatedAt = now });
        }

        if (!await db.NotificationPreferences.AnyAsync(n => n.UserId == userId, cancellationToken))
        {
            db.NotificationPreferences.Add(new NotificationPreferences { UserId = userId, UpdatedAt = now });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
