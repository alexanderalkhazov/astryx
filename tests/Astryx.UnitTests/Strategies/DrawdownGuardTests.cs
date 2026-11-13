using Astryx.Modules.Strategies.Domain;

namespace Astryx.UnitTests.Strategies;

/// <summary>The strategy drawdown guard (SYSTEM_DESIGN.md §7.8, T-097): warn at 90 % of −6.0 %, pause at −6.0 %.</summary>
public class DrawdownGuardTests
{
    private static readonly DateOnly End = new(2026, 11, 9);

    /// <summary>A track whose high is <paramref name="high"/> <paramref name="highDaysAgo"/> days before the last point.</summary>
    private static List<(DateOnly, decimal)> Track(decimal high, int highDaysAgo, decimal last) =>
    [
        (End.AddDays(-40), 90_000m),
        (End.AddDays(-highDaysAgo), high),
        (End.AddDays(-1), (high + last) / 2),
        (End, last),
    ];

    [Theory]
    [InlineData("100000", "99000", "active", "-0.01", "active")]
    [InlineData("100000", "94600", "active", "-0.054", "active_guarded")] // design: "−5.4% · pauses at −6.0%"
    [InlineData("100000", "94601", "active", "-0.05399", "active")]
    [InlineData("100000", "94000", "active", "-0.06", "paused")]
    [InlineData("100000", "93000", "active_guarded", "-0.07", "paused")]
    [InlineData("100000", "99500", "active_guarded", "-0.005", "active")] // recovered
    [InlineData("100000", "100000", "paused", "0", "paused")] // a pause is never lifted automatically
    public void Status_follows_the_drawdown_from_the_window_high(string high, string last, string current, string drawdown, string expected)
    {
        var result = DrawdownGuard.Evaluate(Track(decimal.Parse(high), 10, decimal.Parse(last)), current)!;

        result.Drawdown.ShouldBe(decimal.Parse(drawdown));
        result.Status.ShouldBe(expected);
        result.AsOf.ShouldBe(End);
    }

    [Fact]
    public void A_high_outside_the_30_day_window_does_not_count()
    {
        // The 100k high was 30 days before the last point, just outside a window of the last 30 days.
        var result = DrawdownGuard.Evaluate(Track(100_000m, 30, 94_000m), StrategyStatus.Active)!;

        result.Drawdown.ShouldBe(-0.030928m); // against the 97 000 midpoint, the highest point inside the window
        result.Status.ShouldBe(StrategyStatus.Active);
        DrawdownGuard.Evaluate(Track(100_000m, 29, 94_000m), StrategyStatus.Active)!.Status.ShouldBe(StrategyStatus.Paused);
    }

    [Fact]
    public void Thresholds_are_per_strategy_and_order_does_not_matter()
    {
        var track = Track(100_000m, 5, 96_000m);
        track.Reverse();

        DrawdownGuard.Evaluate(track, StrategyStatus.Active, pausePct: -0.04m)!.Status.ShouldBe(StrategyStatus.Paused);
        DrawdownGuard.Evaluate(track, StrategyStatus.Active, pausePct: -0.08m)!.Status.ShouldBe(StrategyStatus.Active);
        DrawdownGuard.WarnAt(-0.06m).ShouldBe(-0.054m);
        DrawdownGuard.Evaluate([], StrategyStatus.Active).ShouldBeNull();
    }
}
