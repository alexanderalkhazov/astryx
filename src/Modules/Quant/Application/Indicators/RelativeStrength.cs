namespace Astryx.Modules.Quant.Application.Indicators;

/// <summary>
/// Relative strength (SYSTEM_DESIGN.md §13.3): a weighted rate of change over about a quarter, half, three quarters
/// and a year, percentile-ranked 1–99 across the universe (Discover "Rel Str").
/// </summary>
public static class RelativeStrength
{
    /// <summary>Bars needed: ROC over 252 sessions looks 252 bars back from the last one.</summary>
    public const int MinBars = 253;

    private static readonly (int Period, decimal Weight)[] Legs = [(63, 0.4m), (126, 0.2m), (189, 0.2m), (252, 0.2m)];

    /// <summary><c>0.4·ROC63 + 0.2·ROC126 + 0.2·ROC189 + 0.2·ROC252</c> at the last close; null with fewer than 253 bars.</summary>
    public static decimal? Score(IReadOnlyList<decimal> closes)
    {
        if (closes.Count < MinBars)
        {
            return null;
        }

        var last = closes[^1];
        var score = 0m;
        foreach (var (period, weight) in Legs)
        {
            var then = closes[^(period + 1)];
            if (then == 0m)
            {
                return null;
            }

            score += weight * (last / then - 1m);
        }

        return score;
    }

    /// <summary>
    /// Percentile rank 1–99: <c>1 + 98 × (scores strictly below) / (N − 1)</c>, rounded half-even, so the weakest is 1,
    /// the strongest 99 and ties share a rank. A universe of one ranks 50.
    /// </summary>
    public static IReadOnlyDictionary<string, int> Rank(IReadOnlyDictionary<string, decimal> scores)
    {
        if (scores.Count == 1)
        {
            return scores.ToDictionary(s => s.Key, _ => 50);
        }

        var sorted = scores.Values.Order().ToArray();
        return scores.ToDictionary(
            s => s.Key,
            s =>
            {
                var below = LowerBound(sorted, s.Value);
                return (int)Math.Round(1m + 98m * below / (scores.Count - 1), MidpointRounding.ToEven);
            });
    }

    /// <summary>Count of values strictly below <paramref name="value"/> in an ascending array.</summary>
    private static int LowerBound(decimal[] sorted, decimal value)
    {
        int lo = 0, hi = sorted.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (sorted[mid] < value)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }
}
