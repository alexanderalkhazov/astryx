namespace Astryx.Modules.Portfolio.Domain;

/// <summary>Trade outcomes over a period: win rate, average win and loss, and count (SYSTEM_DESIGN.md §11.8 P5).</summary>
public sealed record TradeStats(decimal? WinRate, decimal? AvgWin, decimal? AvgLoss, int Trades);

/// <summary>
/// Performance metrics from EOD snapshots and closed positions (§15.6, §20.11). Pure and decimal-only. TWR chains
/// daily returns, Π(1 + r_d) − 1 with r_d = E_d / E_{d−1} − 1, so deposits and resets don't distort it.
/// </summary>
public static class PerformanceMath
{
    public static decimal? TimeWeightedReturn(IReadOnlyList<decimal> equityByDay)
    {
        if (equityByDay.Count < 2)
        {
            return null;
        }

        var growth = 1m;
        for (var i = 1; i < equityByDay.Count; i++)
        {
            if (equityByDay[i - 1] <= 0)
            {
                return null;
            }

            growth *= equityByDay[i] / equityByDay[i - 1];
        }

        return growth - 1;
    }

    /// <summary>The deepest fall from a running peak, as a non-positive fraction (−0.034 = −3.4%).</summary>
    public static decimal MaxDrawdown(IReadOnlyList<decimal> equity)
    {
        var peak = 0m;
        var worst = 0m;
        foreach (var e in equity)
        {
            peak = Math.Max(peak, e);
            if (peak > 0)
            {
                worst = Math.Min(worst, (e / peak) - 1);
            }
        }

        return worst;
    }

    public static TradeStats Trades(IReadOnlyList<decimal> realizedPnl)
    {
        if (realizedPnl.Count == 0)
        {
            return new TradeStats(null, null, null, 0);
        }

        var wins = realizedPnl.Where(p => p > 0).ToList();
        var losses = realizedPnl.Where(p => p <= 0).ToList();
        return new TradeStats(
            (decimal)wins.Count / realizedPnl.Count,
            wins.Count == 0 ? null : wins.Average(),
            losses.Count == 0 ? null : losses.Average(),
            realizedPnl.Count);
    }

    /// <summary>A benchmark series rebased to start at the portfolio's first equity value, for one chart.</summary>
    public static IReadOnlyList<decimal?> Rebase(IReadOnlyList<decimal?> benchmark, decimal startEquity)
    {
        var first = benchmark.FirstOrDefault(b => b is > 0);
        return first is not { } baseValue ? benchmark.Select(_ => (decimal?)null).ToList() : benchmark.Select(b => b is { } v ? v / baseValue * startEquity : (decimal?)null).ToList();
    }
}
