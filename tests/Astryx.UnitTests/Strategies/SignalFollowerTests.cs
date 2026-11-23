using System.Text.Json.Nodes;
using Astryx.Contracts.Discovery;
using Astryx.Contracts.MarketData;
using Astryx.Modules.Strategies.Application;

namespace Astryx.UnitTests.Strategies;

/// <summary>Real signals followed forward, scored in R (TASKS.md T-166).</summary>
public class SignalFollowerTests
{
    private static readonly DateOnly Day0 = new(2026, 11, 2);

    private static EmittedSignal Signal(string orderType = "buy_stop") => new("ABC", orderType, Entry: 100m, Stop: 95m, Target: 110m, Day0, DateTimeOffset.UnixEpoch);

    private static DailyBar Bar(int day, decimal open, decimal high, decimal low, decimal close, bool final = true) => new(Day0.AddDays(day), open, high, low, close, 1_000, final);

    private static SignalOutcome Follow(params DailyBar[] bars) => SignalFollower.Follow(Signal(), [Bar(0, 98, 99, 97, 98), .. bars], null, []);

    [Fact]
    public void A_buy_stop_fills_the_next_session_and_reaches_its_target_at_plus_2R() =>
        Follow(Bar(1, 99, 101, 98, 100.5m), Bar(2, 101, 111, 100, 109)).ShouldSatisfyAllConditions(
            o => o.Outcome.ShouldBe("target"), o => o.EntryDate.ShouldBe(Day0.AddDays(1)), o => o.R.ShouldBe(2m)); // (110 − 100) / 5

    [Fact]
    public void A_gap_through_the_stop_loses_more_than_1R() =>
        Follow(Bar(1, 99, 101, 98, 100), Bar(2, 92, 93, 90, 91)).ShouldSatisfyAllConditions(o => o.Outcome.ShouldBe("stop_gap"), o => o.R.ShouldBe(-1.6m)); // (92 − 100) / 5

    [Fact]
    public void An_entry_that_is_never_reached_is_not_triggered_and_a_bar_touching_both_stops_first()
    {
        Follow(Bar(1, 97, 99.5m, 96, 98)).Outcome.ShouldBe(SignalFollower.NotTriggered);
        Follow(Bar(1, 99, 101, 98, 100), Bar(2, 100, 111, 94, 105)).Outcome.ShouldBe("stop");
    }

    [Fact]
    public void Still_open_marks_to_the_last_final_close_and_ignores_the_forming_bar() =>
        Follow(Bar(1, 99, 101, 98, 100), Bar(2, 100, 104, 99, 103), Bar(3, 103, 120, 103, 119, final: false)).ShouldSatisfyAllConditions(
            o => o.Outcome.ShouldBe(SignalFollower.Open), o => o.R.ShouldBe(0.6m), o => o.Closed.ShouldBeFalse());

    [Fact]
    public void No_session_after_the_signal_is_pending() => Follow().Outcome.ShouldBe(SignalFollower.Pending);

    [Fact]
    public void The_aggregate_counts_closed_signals_and_the_deepest_R_drawdown()
    {
        SignalOutcome Closed(int day, decimal r) => new("X", Day0.AddDays(day), r > 0 ? "target" : "stop", Day0.AddDays(day + 1), Day0.AddDays(day + 2), r);
        var stats = SignalFollower.Aggregate([Closed(0, 2m), Closed(1, -1m), Closed(2, -1m), Closed(3, 2m), new("Y", Day0, SignalFollower.NotTriggered, null, null, null)]);

        (stats.Signals, stats.Triggered, stats.Closed, stats.WinRate, stats.AverageR, stats.TotalR, stats.MaxDrawdownR).ShouldBe((5, 4, 4, 0.5m, 0.5m, 2m, 2m));
    }

    [Fact]
    public void Beginners_see_a_strategy_only_after_its_trial_and_within_its_backtest_drawdown()
    {
        var today = Day0.AddDays(90);
        TrackRecordStats Live(int closed, decimal drawdown) => new(Day0, today, closed, closed, closed, 0, 0.5m, 0.2m, 4m, drawdown);

        SignalFollower.SoakFor(Live(25, 4m), null, today).Met.ShouldBeFalse();
        SignalFollower.SoakFor(Live(12, 4m), 8m, today).Note.ShouldContain("12 of 20 closed signals");
        SignalFollower.SoakFor(Live(25, 9m), 8m, today).Met.ShouldBeFalse();
        SignalFollower.SoakFor(Live(25, 4m), 8m, today).Met.ShouldBeTrue();
    }
}
