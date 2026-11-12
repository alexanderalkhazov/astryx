using System.Globalization;

namespace Astryx.Modules.Ai.Application.Validation;

/// <summary>The AI's suggested levels for a long, as decimals.</summary>
public sealed record SuggestedLevels(decimal EntryLow, decimal EntryHigh, decimal Stop, decimal Target);

/// <summary>
/// Deterministic checks on AI-suggested levels (SYSTEM_DESIGN.md §12.7 step 6): order, price band, stop distance in
/// ATR, entry-zone width and reward/risk. R:R uses the top of the entry zone, the worst fill for a long.
/// Rejected levels leave the analysis readable but unable to seed a proposal.
/// </summary>
public static class LevelValidator
{
    public const decimal MinStopAtr = 0.5m;
    public const decimal MaxStopAtr = 4m;
    public const decimal MaxZoneAtr = 1m;

    public static LevelValidation Validate(SuggestedLevels levels, decimal bandMin, decimal bandMax, decimal? atr14, decimal minRewardRisk)
    {
        var reasons = new List<string>();
        var (low, high, stop, target) = (levels.EntryLow, levels.EntryHigh, levels.Stop, levels.Target);

        var ordered = stop < low && low <= high && high < target;
        if (!ordered)
        {
            reasons.Add($"Levels must satisfy stop < entry zone < target for a long (stop {P(stop)}, entry {P(low)}–{P(high)}, target {P(target)}).");
        }

        foreach (var (name, value) in new[] { ("Entry low", low), ("Entry high", high), ("Stop", stop), ("Target", target) })
        {
            if (value < bandMin || value > bandMax)
            {
                reasons.Add($"{name} {P(value)} is outside the price band {P(bandMin)}–{P(bandMax)}.");
            }
        }

        decimal? rewardRisk = null;
        if (high > stop)
        {
            var risk = high - stop;
            rewardRisk = Math.Round((target - high) / risk, 2, MidpointRounding.ToEven);
            if (atr14 is > 0 and { } atr)
            {
                var multiple = risk / atr;
                if (multiple < MinStopAtr)
                {
                    reasons.Add($"Stop is too close ({X(multiple)}× ATR). Minimum {X(MinStopAtr)}× ATR.");
                }
                else if (multiple > MaxStopAtr)
                {
                    reasons.Add($"Stop is too wide ({X(multiple)}× ATR). Maximum {X(MaxStopAtr)}× ATR.");
                }

                if (high - low > MaxZoneAtr * atr)
                {
                    reasons.Add($"Entry zone is wider than {X(MaxZoneAtr)}× ATR ({P(high - low)} vs {P(atr)}).");
                }
            }
            else
            {
                reasons.Add("ATR(14) is unavailable, so the stop distance can't be checked.");
            }

            if (ordered && rewardRisk < minRewardRisk)
            {
                reasons.Add($"Reward/risk {rewardRisk.Value.ToString("F2", CultureInfo.InvariantCulture)} is below the minimum {minRewardRisk.ToString("0.0#", CultureInfo.InvariantCulture)}.");
            }
        }

        return new LevelValidation(reasons.Count == 0, reasons, ordered ? rewardRisk : null);
    }

    private static string P(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);

    private static string X(decimal value) => Math.Round(value, 2, MidpointRounding.ToEven).ToString("0.0#", CultureInfo.InvariantCulture);
}
