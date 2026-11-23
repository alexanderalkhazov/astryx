using System.Text.Json.Nodes;
using Astryx.Contracts.MarketData;
using Astryx.Contracts.Quant;
using Astryx.Modules.Quant.Application.Indicators;
using Astryx.Modules.Strategies.Application.Rules;

namespace Astryx.UnitTests.Strategies;

/// <summary>The four rules approved on 2026-11-23 (TASKS.md T-163, docs/strategies/rules-draft.md) on synthetic histories.</summary>
public class NewStrategyRuleTests
{
    private static readonly IndicatorMath Math = new(new IndicatorCalculator());
    private static readonly DateOnly Start = new(2025, 1, 1);

    /// <summary>Daily bars from closes: open at the previous close, a 1 % range, 1M shares unless given.</summary>
    private static List<DailyBar> Bars(IReadOnlyList<decimal> closes, Func<int, long>? volume = null) =>
        closes.Select((c, i) =>
        {
            var open = i == 0 ? c : closes[i - 1];
            return new DailyBar(Start.AddDays(i), open, System.Math.Max(open, c) * 1.005m, System.Math.Min(open, c) * 0.995m, c, volume?.Invoke(i) ?? 1_000_000, Final: true);
        }).ToList();

    private static QuantFacts Facts(List<DailyBar> bars, decimal atr = 2m, int? rank = 90, string? trend = "uptrend", decimal relVolume = 1.5m) =>
        new(new DailyHistory(Guid.NewGuid(), "TEST", bars, null, Stale: false, []), atr, relVolume, rank, trend);

    private static List<decimal> Growth(int count, decimal start, decimal daily) =>
        Enumerable.Range(0, count).Select(i => System.Math.Round(start * DecimalPow(1m + daily, i), 4)).ToList();

    private static decimal DecimalPow(decimal x, int n)
    {
        var r = 1m;
        for (var i = 0; i < n; i++)
        {
            r *= x;
        }

        return r;
    }

    // ---- Trend Momentum ------------------------------------------------------------------------------------------

    private static readonly JsonObject MomentumParams = new() { ["emaPeriod"] = 21, ["minRelStrength"] = 70, ["stopAtr"] = "2.00" };

    private static List<decimal> AcceleratingTrend()
    {
        var closes = Growth(295, 100m, 0.002m);
        for (var i = 0; i < 5; i++)
        {
            closes.Add(System.Math.Round(closes[^1] * 1.01m, 4)); // the last week speeds up: MACD histogram rising
        }

        return closes;
    }

    [Fact]
    public void Momentum_signals_a_leader_above_a_rising_EMA_near_its_high_with_a_3R_target()
    {
        var bars = Bars(AcceleratingTrend());

        var c = new TrendMomentumRule(Math).Evaluate(Facts(bars), MomentumParams).Candidate.ShouldNotBeNull();

        (c.SignalType, c.OrderType).ShouldBe(("momentum", "buy_stop"));
        c.Entry.ShouldBe(System.Math.Ceiling(bars[^1].High * 1.002m * 10m) / 10m);
        c.Stop.ShouldBe(System.Math.Round(c.Entry - 2m * 2m, 2));
        c.Target.ShouldBe(System.Math.Round(c.Entry + 3m * (c.Entry - c.Stop), 2));
        c.RewardRisk.ShouldBe(3.00m);
    }

    [Theory]
    [InlineData(60, "uptrend", "weak_relative_strength")]
    [InlineData(90, "sideways", "not_uptrend")]
    public void Momentum_needs_strength_and_an_uptrend(int rank, string trend, string code) =>
        new TrendMomentumRule(Math).Evaluate(Facts(Bars(AcceleratingTrend()), rank: rank, trend: trend), MomentumParams).Miss!.Code.ShouldBe(code);

    [Fact]
    public void Momentum_skips_a_stock_that_fell_below_its_EMA()
    {
        var closes = AcceleratingTrend();
        closes[^1] = closes[^1] * 0.85m;

        new TrendMomentumRule(Math).Evaluate(Facts(Bars(closes)), MomentumParams).Miss!.Code.ShouldBe("below_ema");
    }

    // ---- Trend Pullback ------------------------------------------------------------------------------------------

    private static readonly JsonObject PullbackParams = new() { ["smaPeriod"] = 50, ["rsiReset"] = 45, ["stopAtr"] = "1.50" };

    [Fact]
    public void Pullback_needs_a_turn_after_an_RSI_reset_near_the_50_day()
    {
        // A steady rise never resets RSI: the rule waits for a pullback.
        new TrendPullbackRule(Math).Evaluate(Facts(Bars(Growth(260, 100m, 0.002m))), PullbackParams).Miss!.Code.ShouldBe("rsi_not_reset");
    }

    [Fact]
    public void Pullback_refuses_a_stock_below_its_200_day()
    {
        var closes = Growth(200, 200m, 0.001m).Concat(Growth(60, 200m, -0.004m)).ToList();

        new TrendPullbackRule(Math).Evaluate(Facts(Bars(closes)), PullbackParams).Miss!.Code.ShouldBeOneOf("sma_not_rising", "below_sma200");
    }

    // ---- RSI(2) Mean Reversion -------------------------------------------------------------------------------------

    private static readonly JsonObject Rsi2Params = new() { ["entryBelow"] = 10, ["exitAbove"] = 70, ["maxHoldDays"] = 10 };

    private static List<decimal> UptrendThenDip()
    {
        var closes = Growth(250, 100m, 0.002m);
        for (var i = 0; i < 3; i++)
        {
            closes.Add(System.Math.Round(closes[^1] * 0.98m, 4)); // three sharp down days
        }

        return closes;
    }

    [Fact]
    public void Rsi2_buys_a_sharp_dip_in_an_uptrend_with_a_limit_under_the_close_and_the_5_day_average_as_target()
    {
        var bars = Bars(UptrendThenDip());

        var c = new Rsi2MeanReversionRule(Math).Evaluate(Facts(bars), Rsi2Params).Candidate.ShouldNotBeNull();

        (c.SignalType, c.OrderType).ShouldBe(("pullback", "limit"));
        c.Entry.ShouldBe(System.Math.Round(bars[^1].Close * 0.995m, 2));
        c.Stop.ShouldBe(System.Math.Round(c.Entry - 2.5m * 2m, 2));
        c.Target.ShouldBe(System.Math.Round(bars.Skip(bars.Count - 5).Average(b => b.Close), 2));
    }

    [Fact]
    public void Rsi2_ignores_thin_stocks_and_crashes()
    {
        new Rsi2MeanReversionRule(Math).Evaluate(Facts(Bars(UptrendThenDip(), _ => 10_000)), Rsi2Params).Miss!.Code.ShouldBe("illiquid");

        var crash = Growth(250, 100m, 0.002m);
        for (var i = 0; i < 5; i++)
        {
            crash.Add(System.Math.Round(crash[^1] * 0.95m, 4)); // −23 % in a week: news, not a dip
        }

        new Rsi2MeanReversionRule(Math).Evaluate(Facts(Bars(crash)), Rsi2Params).Miss!.Code.ShouldBeOneOf("crash_filter", "below_sma200");
    }

    [Fact]
    public void Rsi2_exits_on_a_rebound_or_after_its_holding_period()
    {
        var rule = new Rsi2MeanReversionRule(Math);
        var dip = new QuantFactsView(UptrendThenDip());
        var rebound = new QuantFactsView(UptrendThenDip().Concat([UptrendThenDip()[^1] * 1.03m, UptrendThenDip()[^1] * 1.06m]).ToList());

        rule.ExitReason(dip, sessionsHeld: 2, Rsi2Params).ShouldBeNull();
        rule.ExitReason(rebound, sessionsHeld: 2, Rsi2Params).ShouldBe("rsi_exit");
        rule.ExitReason(dip, sessionsHeld: 10, Rsi2Params).ShouldBe("time_exit");
    }

    // ---- Earnings Drift -----------------------------------------------------------------------------------------

    private static readonly JsonObject DriftParams = new() { ["minGapPct"] = "4.0", ["volumeMultiple"] = "2.0", ["holdDays"] = 20 };

    /// <summary>40 flat sessions at 100, a gap day opening at 106 on 3× volume, then a session holding near 107.</summary>
    private static List<DailyBar> GapAndHold(decimal lastClose = 107m)
    {
        var bars = Bars(Enumerable.Repeat(100m, 40).ToList());
        bars.Add(new DailyBar(Start.AddDays(40), 106m, 108m, 105m, 107m, 3_000_000, Final: true));
        bars.Add(new DailyBar(Start.AddDays(41), 107m, 109m, 106m, lastClose, 1_500_000, Final: true));
        return bars;
    }

    [Fact]
    public void Earnings_drift_buys_a_held_gap_with_the_stop_under_the_gap_day()
    {
        var c = new EarningsDriftRule().Evaluate(Facts(GapAndHold(), rank: 70), DriftParams).Candidate.ShouldNotBeNull();

        (c.SignalType, c.OrderType).ShouldBe(("earnings", "buy_stop"));
        c.Entry.ShouldBe(System.Math.Ceiling(109m * 1.002m * 10m) / 10m); // above the high since the gap
        c.Stop.ShouldBe(105m - 0.25m * 2m);
        c.Target.ShouldBe(System.Math.Round(c.Entry + 2.5m * (c.Entry - c.Stop), 2));
        c.Reason.ShouldBe("Gapped up 6.0% on 3.0× volume yesterday and is holding the gap.");
    }

    [Fact]
    public void Earnings_drift_drops_a_gap_that_failed()
    {
        new EarningsDriftRule().Evaluate(Facts(GapAndHold(lastClose: 104m), rank: 70), DriftParams).Miss!.Code.ShouldBe("gap_filled");
        new EarningsDriftRule().Evaluate(Facts(Bars(Enumerable.Repeat(100m, 45).ToList()), rank: 70), DriftParams).Miss!.Code.ShouldBe("no_gap");
    }

    [Fact]
    public void Earnings_drift_exits_after_its_holding_period() =>
        new EarningsDriftRule().ExitReason(new QuantFactsView([1m]), sessionsHeld: 20, DriftParams).ShouldBe("time_exit");
}
