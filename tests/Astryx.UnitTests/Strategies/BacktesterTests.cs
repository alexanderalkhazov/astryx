using System.Text.Json.Nodes;
using Astryx.Contracts.MarketData;
using Astryx.Contracts.Quant;
using Astryx.Modules.Strategies.Application.Backtesting;
using Astryx.Modules.Strategies.Application.Rules;

namespace Astryx.UnitTests.Strategies;

/// <summary>The backtester's fill, exit, sizing and no-look-ahead rules on hand-checkable bars (TASKS.md T-160).</summary>
public class BacktesterTests
{
    private static readonly DateOnly D0 = new(2026, 1, 5);

    private static DateOnly Day(int n) => D0.AddDays(n);

    /// <summary>Signals once, on <c>signalDay</c>, with fixed levels: isolates the backtester from rule logic.</summary>
    private sealed class OneSignal(DateOnly signalDay, decimal entry, decimal stop, decimal target, string orderType = "buy_stop") : IStrategyRule
    {
        public string Code => "test";

        public string CodeVersion => "test@1";

        public RuleResult Evaluate(QuantFacts facts, JsonObject parameters) =>
            facts.History.Bars[^1].SessionDate == signalDay
                ? RuleResult.Signal(new SignalCandidate("test", facts.History.Symbol, "breakout", orderType, entry, stop, target, (target - entry) / (entry - stop), 80, new Dictionary<string, decimal>(), "test", signalDay))
                : RuleResult.None("no", "no");
    }

    /// <summary>One symbol; every bar has a rank so the rule runs from the first day.</summary>
    private static BacktestDataset Data(params (decimal O, decimal H, decimal L, decimal C)[] bars)
    {
        var daily = bars.Select((b, i) => new DailyBar(Day(i), b.O, b.H, b.L, b.C, 1_000_000, Final: true)).ToList();
        var n = daily.Count;
        var series = new BacktestSeries(Guid.NewGuid(), "AAA", "Tech", daily, new decimal?[n], new decimal?[n], new decimal?[n], new string?[n], Enumerable.Repeat<int?>(90, n).ToArray());
        return new BacktestDataset([series], daily.Select(b => b.SessionDate).ToList());
    }

    private static BacktestResult Run(BacktestDataset data, IStrategyRule rule) => Backtester.Run(data, rule, [], new BacktestSettings());

    [Fact]
    public void A_buy_stop_fills_the_next_session_and_exits_at_the_target_with_the_live_size_and_costs()
    {
        var data = Data((100, 100, 99, 100), (100, 100.5m, 99, 100), (100, 102, 99.5m, 101.5m), (105, 111, 104, 110.5m), (110, 111, 109, 110));

        var trade = Run(data, new OneSignal(Day(1), entry: 101m, stop: 95m, target: 110m)).Trades.ShouldHaveSingleItem();

        // Fill: max(open 100, entry 101) + 1 bp rounded up to the cent (0.02). Exit: max(open 105, 110) − 0.02.
        (trade.EntryDate, trade.EntryPrice, trade.ExitDate, trade.ExitPrice, trade.ExitReason).ShouldBe((Day(2), 101.02m, Day(3), 109.98m, "target"));
        // Size: risk bound 124 (750 / 6.04), weight bound 118 (12 000 / 101), cash bound 989 → 118 shares.
        trade.Quantity.ShouldBe(118m);
        trade.Pnl.ShouldBe(118m * (109.98m - 101.02m));
        Math.Round(trade.RMultiple, 4).ShouldBe(Math.Round(8.96m / 6.02m, 4));
    }

    [Fact]
    public void A_gap_below_the_stop_exits_at_the_open()
    {
        var data = Data((100, 100, 99, 100), (100, 100.5m, 99, 100), (100, 102, 99.5m, 101.5m), (90, 92, 88, 91));

        var trade = Run(data, new OneSignal(Day(1), 101m, 95m, 110m)).Trades.ShouldHaveSingleItem();

        (trade.ExitPrice, trade.ExitReason).ShouldBe((89.99m, "stop_gap")); // min(open 90, stop 95) − 0.01
    }

    [Fact]
    public void An_untriggered_buy_stop_lapses_at_the_close()
    {
        var data = Data((100, 100, 99, 100), (100, 100.5m, 99, 100), (100, 100.9m, 99.5m, 100.5m), (100, 105, 99, 104));

        Run(data, new OneSignal(Day(1), 101m, 95m, 110m)).Trades.ShouldBeEmpty(); // day 3's rally is too late: the order was day 2's
    }

    [Fact]
    public void A_bar_that_touches_both_stop_and_target_counts_as_the_stop()
    {
        var data = Data((100, 100, 99, 100), (100, 100.5m, 99, 100), (100, 102, 99.5m, 101.5m), (101, 112, 94, 100));

        Run(data, new OneSignal(Day(1), 101m, 95m, 110m)).Trades.ShouldHaveSingleItem().ExitReason.ShouldBe("stop");
    }

    [Fact]
    public void An_open_position_at_the_end_is_marked_to_the_last_close()
    {
        var data = Data((100, 100, 99, 100), (100, 100.5m, 99, 100), (100, 102, 99.5m, 101.5m), (101, 103, 100, 102.5m));

        var result = Run(data, new OneSignal(Day(1), 101m, 95m, 110m));

        var trade = result.Trades.ShouldHaveSingleItem();
        (trade.ExitPrice, trade.ExitReason).ShouldBe((102.5m, "end_of_data"));
        result.Equity[^1].Equity.ShouldBe(100_000m + trade.Pnl);
    }

    [Fact]
    public void The_facts_at_a_session_end_at_that_session()
    {
        var data = Data(Enumerable.Range(0, 400).Select(i => (100m + i, 101m + i, 99m + i, 100.5m + i)).ToArray());
        var series = data.Series[0];

        var facts = Backtester.FactsAt(series, 350);

        facts.History.Bars[^1].SessionDate.ShouldBe(series.Bars[350].SessionDate); // nothing after bar 350
        facts.History.Bars.Count.ShouldBe(Backtester.HistoryWindow);
        facts.RelStrengthRank.ShouldBe(series.RelStrengthRank[350]);
    }

    [Fact]
    public void Changing_later_bars_does_not_change_earlier_decisions()
    {
        var bars = new (decimal, decimal, decimal, decimal)[] { (100, 100, 99, 100), (100, 100.5m, 99, 100), (100, 102, 99.5m, 101.5m), (105, 111, 104, 110.5m), (110, 111, 109, 110) };
        var changed = bars.ToArray();
        changed[4] = (50, 51, 40, 45); // a crash after the trade closed

        var first = Run(Data(bars), new OneSignal(Day(1), 101m, 95m, 110m)).Trades.Single();
        var second = Run(Data(changed), new OneSignal(Day(1), 101m, 95m, 110m)).Trades.Single();

        second.ShouldBe(first with { InstrumentId = second.InstrumentId });
    }

    [Fact]
    public void Metrics_report_growth_drawdown_and_win_rate()
    {
        var result = new BacktestResult(
            [new SimulatedTrade(Guid.Empty, "AAA", Day(0), Day(1), 100m, 110m, 10m, 95m, 100m, 2m, "target"), new SimulatedTrade(Guid.Empty, "AAA", Day(2), Day(3), 100m, 95m, 10m, 95m, -50m, -1m, "stop")],
            [(Day(0), 100_000m), (Day(1), 110_000m), (Day(2), 99_000m), (Day(3), 121_000m)],
            Day(3));

        var full = BacktestMetrics.Compute(result, 100_000m)["full"]!;

        ((string)full["maxDrawdown"]!).ShouldBe("-0.100"); // 110 000 → 99 000
        ((string)full["winRate"]!).ShouldBe("0.50");
        ((string)full["multiple"]!).ShouldBe("1.21");
        ((string)full["profitFactor"]!).ShouldBe("2.00");
        ((int)full["trades"]!).ShouldBe(2);
    }

    [Theory]
    [InlineData("2", "1.41421356237")]
    [InlineData("0.25", "0.50000000000")]
    public void Square_root(string x, string expected) => Math.Round(DecimalMath.Sqrt(decimal.Parse(x, System.Globalization.CultureInfo.InvariantCulture)), 11).ShouldBe(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void Logarithm_exponential_and_power()
    {
        Math.Round(DecimalMath.Ln(10m), 11).ShouldBe(2.30258509299m);
        Math.Round(DecimalMath.Exp(1m), 11).ShouldBe(2.71828182846m);
        Math.Round(DecimalMath.Pow(1.21m, 0.5m), 11).ShouldBe(1.1m);
        Math.Round(DecimalMath.Exp(DecimalMath.Ln(0.03m)), 12).ShouldBe(0.03m);
    }
}
