using Astryx.Contracts.MarketData;
using Astryx.Modules.Quant.Application.Analysis;
using Astryx.Modules.Quant.Application.Indicators;

namespace Astryx.UnitTests.Quant;

/// <summary>Snapshot assembly and data quality (SYSTEM_DESIGN.md §13.5, TASKS.md T-061).</summary>
public class StockAnalysisEngineTests
{
    private static readonly StockAnalysisEngine Engine = new(new IndicatorCalculator());

    private static DailyHistory History(int bars, bool stale = false, int gaps = 0) => new(
        Guid.NewGuid(),
        "TEST",
        Enumerable.Range(0, bars).Select(i => new DailyBar(new DateOnly(2026, 1, 1).AddDays(i), 100m + i, 101m + i, 99m + i, 100.5m + i, 1_000_000, Final: true)).ToList(),
        DateTimeOffset.UnixEpoch,
        stale,
        Enumerable.Range(0, gaps).Select(i => new DateOnly(2025, 12, 1).AddDays(i)).ToList());

    [Fact]
    public void Short_history_lists_what_is_missing_and_is_insufficient_but_never_partial()
    {
        var snapshot = Engine.Analyze(History(30), relStrengthRank: null);

        snapshot.Insufficient.ShouldBe(["sma50", "macd", "macdSignal", "macdHist", "relStrength", "trend"], ignoreOrder: true); // MACD needs 34 bars as a group
        snapshot.Values["sma50"].ShouldBeNull();
        snapshot.Values["sma20"].ShouldNotBeNull();
        snapshot.DataQuality.ShouldBe(DataQuality.Insufficient);
    }

    [Theory]
    [InlineData(false, 0, "ok")]
    [InlineData(false, 1, "gaps")]
    [InlineData(false, 3, "insufficient")] // more than two gaps
    [InlineData(true, 0, "stale")] // stale wins: it blocks proposals
    [InlineData(true, 3, "stale")]
    public void Quality_follows_the_rules(bool stale, int gaps, string expected)
    {
        var snapshot = Engine.Analyze(History(300, stale, gaps), relStrengthRank: 50);

        snapshot.DataQuality.ShouldBe(expected);
    }

    [Fact]
    public void Values_are_presentation_strings_in_a_fixed_order()
    {
        var snapshot = Engine.Analyze(History(300), relStrengthRank: 94);

        snapshot.Values.Keys.ShouldBe(StockAnalysisEngine.ValueNames);
        snapshot.Values["rsi14"].ShouldBe("100.0"); // a straight line up has no losses
        snapshot.Values["relStrength"].ShouldBe("94");
        snapshot.Values["trend"].ShouldBe("uptrend");
        snapshot.Values["sma20"]!.ShouldMatch(@"^\d+\.\d{4}$");
        StockAnalysisEngine.ParamsHash.Length.ShouldBe(16);
    }
}
