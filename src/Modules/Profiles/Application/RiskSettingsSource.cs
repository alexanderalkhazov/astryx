using Astryx.Contracts.Profiles;
using Astryx.Modules.Profiles.Domain;
using Astryx.Modules.Profiles.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Profiles.Application;

/// <summary>A user's risk limits for other modules; workers have no user scope, so the user is addressed explicitly.</summary>
public sealed class RiskSettingsSource(ProfilesDbContext db) : IRiskSettingsSource
{
    public async Task<RiskLimits> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var s = await db.RiskSettings.AsNoTracking().SingleOrDefaultAsync(r => r.UserId == userId, cancellationToken) ?? new RiskSettings();
        return new RiskLimits(s.MaxRiskPerTradePct, s.MaxPositionWeightPct, s.MaxSectorExposurePct, s.SectorWarningPct, s.DailyLossHaltPct, s.MaxOpenPositions, s.MaxGrossExposurePct, s.MinRewardRisk, s.Version);
    }
}
