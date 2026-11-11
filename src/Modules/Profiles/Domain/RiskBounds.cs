namespace Astryx.Modules.Profiles.Domain;

/// <summary>
/// User-editable ranges for risk limits (SYSTEM_DESIGN.md §14.3). The validator, the API's <c>bounds</c> block and the
/// <c>identity.risk_settings</c> CHECK constraints all use these numbers.
/// </summary>
public static class RiskBounds
{
    public static readonly Range<decimal> MaxRiskPerTradePct = new(0.001m, 0.02m);
    public static readonly Range<decimal> MaxPositionWeightPct = new(0.02m, 0.25m);
    public static readonly Range<decimal> MaxSectorExposurePct = new(0.10m, 0.60m);
    public static readonly Range<decimal> DailyLossHaltPct = new(0.005m, 0.05m);
    public static readonly Range<int> MaxOpenPositions = new(1, 30);
    public static readonly Range<decimal> MinRewardRisk = new(1.0m, 5.0m);

    /// <summary>Ratios are stored as numeric(9,6), so more than 6 decimal places would be silently rounded.</summary>
    public const int PctScale = 6;

    public const int RewardRiskScale = 4;
}

public sealed record Range<T>(T Min, T Max);
