using Astryx.Modules.Paper.Domain;

namespace Astryx.UnitTests.Paper;

/// <summary>Working-order triggers (SYSTEM_DESIGN.md §15.2, PAPER-04/05, TASKS.md T-085). NVDA: stop 178.20, target 204.00.</summary>
public class TriggerEvaluatorTests
{
    [Fact]
    public void PAPER_04_a_gap_below_the_stop_exits_at_the_open_minus_slippage()
    {
        var exit = TriggerEvaluator.Exit(178.20m, 204.00m, new PriceWindow(Open: 175.00m, High: 176.00m, Low: 174.50m, Last: 175.80m), 0.02m)!;

        (exit.Purpose, exit.Price, exit.GappedThroughStop).ShouldBe(("exit_stop", 174.98m, true));
    }

    [Fact]
    public void A_tick_below_the_stop_exits_at_that_tick_minus_slippage()
    {
        var exit = TriggerEvaluator.Exit(178.20m, 204.00m, PriceWindow.Tick(175.00m), 0.02m)!;

        (exit.Price, exit.ReferencePrice, exit.GappedThroughStop).ShouldBe((174.98m, 175.00m, true));
        TriggerEvaluator.Exit(178.20m, 204.00m, PriceWindow.Tick(178.20m), 0.02m)!.ShouldSatisfyAllConditions(e => e.Price.ShouldBe(178.18m), e => e.GappedThroughStop.ShouldBeFalse());
    }

    [Fact]
    public void PAPER_05_when_a_bar_touches_both_levels_the_stop_wins()
    {
        var exit = TriggerEvaluator.Exit(178.20m, 204.00m, new PriceWindow(Open: 190m, High: 205m, Low: 177m, Last: 191m), 0.02m)!;

        exit.Purpose.ShouldBe("exit_stop");
    }

    [Fact]
    public void The_target_fills_at_the_target_without_slippage_and_quiet_ticks_do_nothing()
    {
        TriggerEvaluator.Exit(178.20m, 204.00m, PriceWindow.Tick(204.10m), 0.02m)!.ShouldSatisfyAllConditions(e => e.Purpose.ShouldBe("exit_target"), e => e.Price.ShouldBe(204.00m));
        TriggerEvaluator.Exit(178.20m, 204.00m, PriceWindow.Tick(190m), 0.02m).ShouldBeNull();
    }

    [Fact]
    public void Working_entries_trigger_on_ticks_and_gaps()
    {
        TriggerEvaluator.Entry("buy_stop", 186.50m, PriceWindow.Tick(186.40m), 0.02m).ShouldBeNull();
        TriggerEvaluator.Entry("buy_stop", 186.50m, PriceWindow.Tick(186.55m), 0.02m)!.Price.ShouldBe(186.57m);
        TriggerEvaluator.Entry("buy_stop", 186.50m, new PriceWindow(190m, 191m, 189m, 190.5m), 0.02m)!.Price.ShouldBe(190.02m); // gapped above: the open
        TriggerEvaluator.Entry("limit", 180m, PriceWindow.Tick(179.50m), 0.02m)!.Price.ShouldBe(179.50m);
    }

    [Theory]
    [InlineData("180.00", true)] // 1.0% above
    [InlineData("180.91", true)] // 1.4979%
    [InlineData("181.00", false)] // 1.55%
    [InlineData("178.00", false)] // through the stop: an exit, not an alert
    public void Stop_proximity_is_within_one_and_a_half_percent(string last, bool near) =>
        TriggerEvaluator.NearStop(decimal.Parse(last, System.Globalization.CultureInfo.InvariantCulture), 178.20m).ShouldBe(near);
}
