using Astryx.Modules.Paper.Domain;
using Astryx.SharedKernel.Finance;

namespace Astryx.UnitTests.Paper;

/// <summary>Entry fills on a quote tick (SYSTEM_DESIGN.md §15.2, PAPER-01/02, TASKS.md T-084).</summary>
public class PaperFillSimulatorTests
{
    private const decimal Slip = 0.02m; // max($0.01, ceil_cent(186.50 × 1 bp))

    [Fact]
    public void PAPER_01_a_triggered_buy_stop_fills_at_last_plus_slippage()
    {
        var fill = PaperFillSimulator.EntryOnTick("buy_stop", 186.50m, 186.60m, Slip)!;

        (fill.Price, fill.ReferencePrice, fill.SlippagePerShare).ShouldBe((186.62m, 186.60m, 0.02m));
        RoundingPolicy.Ledger(76 * fill.Price).ShouldBe(14_183.12m);
    }

    [Fact]
    public void The_design_trade_fills_at_186_52_and_leaves_7665_03_cash()
    {
        var fill = PaperFillSimulator.EntryOnTick("buy_stop", 186.50m, 186.50m, Slip)!;

        fill.Price.ShouldBe(186.52m);
        (21_840.55m - RoundingPolicy.Ledger(76 * fill.Price)).ShouldBe(7_665.03m);
    }

    [Fact]
    public void PAPER_02_an_untriggered_buy_stop_works_and_reserves_its_worst_fill()
    {
        PaperFillSimulator.EntryOnTick("buy_stop", 186.50m, 186.40m, Slip).ShouldBeNull();
        PaperFillSimulator.Reservation("buy_stop", 76, 186.50m, Slip, 0m).ShouldBe(76 * 186.52m);
    }

    [Fact]
    public void Limits_fill_when_touched_without_adverse_slippage_and_markets_fill_now()
    {
        PaperFillSimulator.EntryOnTick("limit", 180.00m, 180.50m, Slip).ShouldBeNull();
        PaperFillSimulator.EntryOnTick("limit", 180.00m, 179.40m, Slip)!.ShouldSatisfyAllConditions(f => f.Price.ShouldBe(179.40m), f => f.SlippagePerShare.ShouldBe(0m));
        PaperFillSimulator.Reservation("limit", 10, 180.00m, Slip, 1m).ShouldBe(1_801m);
        PaperFillSimulator.EntryOnTick("market", 0m, 50.00m, 0.01m)!.Price.ShouldBe(50.01m);
        Should.Throw<ArgumentOutOfRangeException>(() => PaperFillSimulator.EntryOnTick("sell_stop", 1m, 1m, 0m));
    }
}
