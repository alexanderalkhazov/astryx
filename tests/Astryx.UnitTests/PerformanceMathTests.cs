using Astryx.Modules.Portfolio.Domain;

namespace Astryx.UnitTests;

/// <summary>Performance metrics (SYSTEM_DESIGN.md §15.6, §20.11, TASKS.md T-090).</summary>
public class PerformanceMathTests
{
    [Fact]
    public void TWR_chains_daily_returns()
    {
        PerformanceMath.TimeWeightedReturn([100_000m, 101_000m, 99_990m, 102_000m])!.Value.ShouldBe(0.02m);
        PerformanceMath.TimeWeightedReturn([100_000m]).ShouldBeNull();
    }

    [Fact]
    public void Max_drawdown_is_the_deepest_fall_from_a_running_peak()
    {
        PerformanceMath.MaxDrawdown([100m, 120m, 90m, 130m, 117m]).ShouldBe(-0.25m);
        PerformanceMath.MaxDrawdown([100m, 101m, 102m]).ShouldBe(0m);
    }

    [Fact]
    public void Trade_stats_split_wins_and_losses()
    {
        var stats = PerformanceMath.Trades([1112.32m, -744.96m, 215.04m, 0m]);

        (stats.WinRate, stats.Trades).ShouldBe((0.5m, 4));
        stats.AvgWin.ShouldBe(663.68m);
        stats.AvgLoss.ShouldBe(-372.48m);
        PerformanceMath.Trades([]).ShouldBe(new TradeStats(null, null, null, 0));
    }

    [Fact]
    public void A_benchmark_rebases_to_the_portfolios_first_value()
    {
        PerformanceMath.Rebase([500m, 510m, null, 495m], 100_000m).ShouldBe([100_000m, 102_000m, null, 99_000m]);
    }
}
