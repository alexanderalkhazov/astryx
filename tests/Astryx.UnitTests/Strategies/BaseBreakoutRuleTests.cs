using System.Text.Json.Nodes;
using Astryx.Contracts.MarketData;
using Astryx.Contracts.Quant;
using Astryx.Modules.Strategies.Application.Rules;

namespace Astryx.UnitTests.Strategies;

/// <summary>Base Breakout rule edges (SYSTEM_DESIGN.md §13.4.1, TASKS.md T-064).</summary>
public class BaseBreakoutRuleTests
{
    private static readonly BaseBreakoutRule Rule = new();
    private static JsonObject Params(decimal volumeMultiple = 1.8m) =>
        new() { ["baseWeeks"] = 6, ["volumeMultiple"] = volumeMultiple.ToString(System.Globalization.CultureInfo.InvariantCulture), ["minRelStrength"] = 80, ["stopAtr"] = "1.50" };

    /// <summary>40 flat sessions between 95 and 100 (closing at 99), then today's bar.</summary>
    private static QuantFacts Facts(decimal todayHigh = 101m, decimal relVolume = 2.4m, int? rank = 90, string? trend = "uptrend", decimal baseLow = 95m, decimal atr = 2m)
    {
        var bars = Enumerable.Range(0, 40)
            .Select(i => new DailyBar(new DateOnly(2026, 1, 1).AddDays(i), 98m, i == 20 ? 100m : 99.5m, i == 25 ? baseLow : 97m, 99m, 1_000_000, Final: true))
            .Append(new DailyBar(new DateOnly(2026, 2, 15), 99m, todayHigh, 98.5m, 100.5m, 2_400_000, Final: false))
            .ToList();
        var history = new DailyHistory(Guid.NewGuid(), "TEST", bars, DateTimeOffset.UnixEpoch, Stale: false, []);
        return new QuantFacts(history, atr, relVolume, rank, trend);
    }

    [Fact]
    public void Levels_follow_the_documented_formulas()
    {
        var signal = Rule.Evaluate(Facts(), Params()).Candidate!;

        signal.Entry.ShouldBe(100.30m); // 100 × 1.003, rounded up to the dime
        signal.Stop.ShouldBe(97.30m); // entry − 1.5 × ATR 2
        signal.Target.ShouldBe(110.00m); // 100 + 2 × (100 − 95)
        signal.RewardRisk.ShouldBe(3.23m);
        signal.Reason.ShouldBe("Cleared a 6-week base at $100.00 on 2.4× average volume.");
        signal.AsOfBar.ShouldBe(new DateOnly(2026, 2, 15));
    }

    [Theory]
    [InlineData("no_breakout")]
    [InlineData("volume_too_low")]
    [InlineData("weak_relative_strength")]
    [InlineData("downtrend")]
    [InlineData("base_too_deep")]
    [InlineData("insufficient_history")]
    public void Each_condition_can_veto_the_signal(string expected)
    {
        var facts = expected switch
        {
            "no_breakout" => Facts(todayHigh: 100m), // must clear, not just touch
            "volume_too_low" => Facts(relVolume: 1.79m),
            "weak_relative_strength" => Facts(rank: 79),
            "downtrend" => Facts(trend: "downtrend"),
            "base_too_deep" => Facts(baseLow: 79m), // 21 % deep
            _ => Facts(atr: 0m) with { Atr14 = null },
        };

        var result = Rule.Evaluate(facts, Params());

        result.Candidate.ShouldBeNull();
        result.Miss!.Code.ShouldBe(expected);
    }

    [Fact]
    public void A_sideways_trend_scores_lower_but_still_signals()
    {
        var up = Rule.Evaluate(Facts(), Params()).Candidate!;
        var sideways = Rule.Evaluate(Facts(trend: "sideways"), Params()).Candidate!;

        sideways.Breakdown["trend"].ShouldBe(6.00m); // 15 × 0.4
        sideways.Score.ShouldBeLessThan(up.Score);
    }

    [Fact]
    public void Parameters_change_the_outcome()
    {
        Rule.Evaluate(Facts(relVolume: 2.0m), Params(volumeMultiple: 2.2m)).Miss!.Code.ShouldBe("volume_too_low");
        Rule.Evaluate(Facts(relVolume: 2.0m), Params(volumeMultiple: 1.8m)).Candidate.ShouldNotBeNull();
    }
}
