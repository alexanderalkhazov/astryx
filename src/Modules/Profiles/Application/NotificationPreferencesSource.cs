using Astryx.Contracts.Profiles;
using Astryx.Modules.Profiles.Domain;
using Astryx.Modules.Profiles.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Profiles.Application;

/// <summary>A user's push categories for the push job, which runs without a user scope.</summary>
public sealed class NotificationPreferencesSource(ProfilesDbContext db) : INotificationPreferencesSource
{
    public async Task<PushPreferences> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var n = await db.NotificationPreferences.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken) ?? new NotificationPreferences();
        return new PushPreferences(n.Fills, n.Signals, n.StopProximity, n.StrategyGuard, n.System, n.DailySummary, n.PushEnabled);
    }
}
