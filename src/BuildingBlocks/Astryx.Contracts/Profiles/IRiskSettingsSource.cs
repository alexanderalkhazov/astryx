namespace Astryx.Contracts.Profiles;

/// <summary>A user's risk limits as fractions (0.0075 = 0.75 %), defaults when the user has no row (SYSTEM_DESIGN.md §10.4).</summary>
public sealed record RiskLimits(
    decimal MaxRiskPerTradePct, decimal MaxPositionWeightPct, decimal MaxSectorExposurePct, decimal SectorWarningPct,
    decimal DailyLossHaltPct, int MaxOpenPositions, decimal MaxGrossExposurePct, decimal MinRewardRisk, int Version = 1);

/// <summary>Owned by Profiles; read by Ai (level checks) and later the risk engine through Contracts.</summary>
public interface IRiskSettingsSource
{
    Task<RiskLimits> GetAsync(Guid userId, CancellationToken cancellationToken);
}
