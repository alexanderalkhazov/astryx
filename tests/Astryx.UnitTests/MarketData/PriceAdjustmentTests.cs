using Astryx.Contracts.MarketData;
using Astryx.Modules.MarketData.Domain;

namespace Astryx.UnitTests.MarketData;

/// <summary>Back-adjustment over unadjusted bars (SYSTEM_DESIGN.md §20.7, TASKS.md T-044).</summary>
public class PriceAdjustmentTests
{
    private static readonly DateOnly Ex = new(2026, 6, 10);

    private static CorporateAction Split(decimal ratio, DateOnly? ex = null) =>
        new() { InstrumentId = Guid.Empty, ActionType = ratio >= 1 ? CorporateActionType.Split : CorporateActionType.ReverseSplit, ExDate = ex ?? Ex, Ratio = ratio, Source = "test" };

    private static CorporateAction Dividend(decimal cash, DateOnly? ex = null) =>
        new() { InstrumentId = Guid.Empty, ActionType = CorporateActionType.CashDividend, ExDate = ex ?? Ex, CashAmount = cash, Source = "test" };

    [Fact]
    public void A_4_for_1_split_divides_earlier_prices_by_4_and_multiplies_volume_by_4()
    {
        var factors = PriceAdjustment.Factors([Split(4m)], AdjustmentMode.SplitOnly, _ => null);

        // The day before: a $400 close on 1M shares reads as $100 on 4M, continuous with the post-split price.
        var (price, volume) = PriceAdjustment.At(Ex.AddDays(-1), factors);
        (400m * price, 1_000_000 * volume).ShouldBe((100m, 4_000_000m));
        PriceAdjustment.At(Ex, factors).ShouldBe((1m, 1m)); // from the ex-date on, bars are already post-split
    }

    [Fact]
    public void A_reverse_split_multiplies_earlier_prices()
    {
        var factors = PriceAdjustment.Factors([Split(0.1m)], AdjustmentMode.SplitOnly, _ => null);

        var (price, volume) = PriceAdjustment.At(Ex.AddDays(-1), factors);
        (2m * price, 1_000_000 * volume).ShouldBe((20m, 100_000m));
    }

    [Fact]
    public void A_dividend_adjusts_earlier_prices_in_total_return_mode_only()
    {
        // $2.00 dividend, $100 close the session before: earlier prices × 0.98.
        var total = PriceAdjustment.Factors([Dividend(2m)], AdjustmentMode.TotalReturn, ex => ex == Ex ? 100m : null);
        var splitOnly = PriceAdjustment.Factors([Dividend(2m)], AdjustmentMode.SplitOnly, ex => ex == Ex ? 100m : null);

        PriceAdjustment.At(Ex.AddDays(-1), total).ShouldBe((0.98m, 1m));
        PriceAdjustment.At(Ex.AddDays(-1), splitOnly).ShouldBe((1m, 1m));
        PriceAdjustment.Factors([Dividend(2m)], AdjustmentMode.TotalReturn, _ => null).ShouldBeEmpty(); // no prior close, no guess
    }

    [Fact]
    public void Factors_compound_for_bars_before_several_actions()
    {
        var factors = PriceAdjustment.Factors([Split(2m, Ex), Dividend(1m, Ex.AddDays(30))], AdjustmentMode.TotalReturn, _ => 50m);

        PriceAdjustment.At(Ex.AddDays(-1), factors).Price.ShouldBe(0.5m * 0.98m); // both later actions apply
        PriceAdjustment.At(Ex.AddDays(1), factors).Price.ShouldBe(0.98m); // only the dividend is still ahead
    }
}
