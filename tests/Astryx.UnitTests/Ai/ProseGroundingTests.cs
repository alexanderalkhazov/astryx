using System.Text.Json.Nodes;
using Astryx.Modules.Ai.Application.Context;
using Astryx.Modules.Ai.Application.Validation;

namespace Astryx.UnitTests.Ai;

/// <summary>Numbers in the model's prose must come from the facts (TASKS.md T-165).</summary>
public class ProseGroundingTests
{
    private static AnalysisContext Context() => new(
        "NVDA", "NVIDIA", DateTimeOffset.Parse("2026-11-20T21:00:00Z", System.Globalization.CultureInfo.InvariantCulture), 150m, 230m, [],
        JsonNode.Parse("""{"quote":{"last":"186.40","changePct":"0.0123"},"indicators":{"rsi14":"64.20","sma50":"180.10","atr14":"4.10"}}""")!.AsObject());

    private static JsonObject Analysis(string explanation, params string[] evidence) => new()
    {
        ["explanation"] = explanation,
        ["supportingEvidence"] = new JsonArray(evidence.Select(e => (JsonNode)e).ToArray()),
        ["suggestedLevels"] = new JsonObject { ["entryZone"] = new JsonObject { ["low"] = "186.00", ["high"] = "187.00" }, ["stopLoss"] = "178.20", ["takeProfit"] = "204.00" },
        ["confidence"] = new JsonObject { ["value"] = 0.6 },
    };

    private static readonly LevelValidation Levels = new(true, [], 2.07m);

    [Fact]
    public void Grounded_numbers_rounding_percentages_levels_and_distances_pass()
    {
        var analysis = Analysis(
            "NVDA trades at $186.40, up 1.23% today, with RSI(14) near 64. It sits 3.5% above its 50-day average of 180.10. A stop at 178.20 against a target of $204 gives 2.07 reward to risk.",
            "Price is above the 50-day average.");
        var issues = new List<ValidationIssue>();

        ProseGrounding.Check(analysis, Context(), Levels, 1.5m, issues).ShouldBeGreaterThan(5);

        issues.ShouldBeEmpty();
        analysis["explanation"]!.GetValue<string>().ShouldStartWith("NVDA trades at $186.40");
    }

    [Fact]
    public void A_sentence_with_an_invented_number_is_removed_and_recorded()
    {
        var analysis = Analysis("RSI is 64.2, which is healthy. Analysts expect the stock to reach $250 soon.", "RSI(14) is 82, overbought.", "ATR is 4.10.");
        var issues = new List<ValidationIssue>();

        ProseGrounding.Check(analysis, Context(), Levels, 1.5m, issues);

        analysis["explanation"]!.GetValue<string>().ShouldBe("RSI is 64.2, which is healthy.");
        analysis["supportingEvidence"]!.AsArray().Select(e => e!.GetValue<string>()).ShouldBe(["ATR is 4.10."]);
        issues.Select(i => i.Code).ShouldAllBe(c => c == ValidationIssueCodes.UngroundedNumber);
        issues.Count.ShouldBe(2);
    }

    [Fact]
    public void Text_with_only_invented_numbers_is_replaced_by_a_plain_notice() =>
        Analysis("The stock will gain 40% by March.").ShouldSatisfyAllConditions(a =>
        {
            ProseGrounding.Check(a, Context(), Levels, 1.5m, []);
            a["explanation"]!.GetValue<string>().ShouldBe(ProseGrounding.RemovedText);
        });

    [Theory]
    [InlineData("the 14-day RSI over 2 sessions", 0)]
    [InlineData("RSI(14) is 82", 1)]
    [InlineData("volume 1.5× average, stop 2 ATR below", 0)]
    [InlineData("on 2026-11-20 at 21:00", 0)]
    [InlineData("volume of 12.3M shares", 1)]
    [InlineData("$1,204.50 and 3%", 2)]
    public void Periods_counts_and_dates_are_exempt_but_amounts_are_checked(string text, int checkedNumbers) =>
        ProseGrounding.Find(text).Count(n => !n.Exempt).ShouldBe(checkedNumbers);
}
