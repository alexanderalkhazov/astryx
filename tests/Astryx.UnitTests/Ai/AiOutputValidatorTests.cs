using System.Text.Json.Nodes;
using Astryx.Contracts.MarketData;
using Astryx.Modules.Ai.Application.Analysis;
using Astryx.Modules.Ai.Application.Context;
using Astryx.Modules.Ai.Application.Prompts;
using Astryx.Modules.Ai.Application.Providers;
using Astryx.Modules.Ai.Application.Validation;
using Astryx.Modules.Ai.Infrastructure.Fake;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Astryx.UnitTests.Ai;

/// <summary>Every §12.7 validation step and the repair retry (SYSTEM_DESIGN.md §12.6–12.7, TASKS.md T-072).</summary>
public class AiOutputValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 19, 32, 41, TimeSpan.Zero);
    private static readonly PromptTemplate Prompt = Stubs.Registry().GetActive(PromptRegistry.StockAnalysis);
    private readonly AiOutputValidator _validator = new();

    private static async Task<ValidationInput> InputAsync(bool fresh = true, decimal minRewardRisk = 1.5m) =>
        new(await new Stubs().BuildAsync(), Prompt, "qwen3.5:0.8b", Now, fresh, minRewardRisk);

    private static string Valid() => FakeAiProvider.Fixture(FakeAiScenario.Valid);

    private static string Edit(Action<JsonObject> change)
    {
        var json = JsonNode.Parse(Valid())!.AsObject();
        change(json);
        return json.ToJsonString();
    }

    [Fact]
    public async Task The_design_analysis_is_valid_and_gets_the_backend_fields()
    {
        var result = _validator.Validate(Valid(), await InputAsync());

        result.Status.ShouldBe(ValidationStatus.Valid, string.Join("; ", result.Issues.Select(i => i.Message)));
        var a = result.Analysis!;
        a["analysisTimestamp"]!.GetValue<string>().ShouldBe("2026-11-09T19:32:41Z");
        a["rewardRisk"]!.GetValue<string>().ShouldBe("1.77"); // (204.00 − 187.50) ÷ (187.50 − 178.20), worst fill
        a["levelValidation"]!.ToJsonString().ShouldBe("""{"accepted":true,"reasons":[]}""");
        a["suggestedLevels"]!["accepted"]!.GetValue<bool>().ShouldBeTrue();
        a["stale"]!.GetValue<bool>().ShouldBeFalse();
        (a["model"]!.GetValue<string>(), a["promptVersion"]!.GetValue<string>()).ShouldBe(("qwen3.5:0.8b", "stock_analysis/2026-11-09.1"));
        a["disclaimer"]!.GetValue<string>().ShouldStartWith("AI-generated research for information only.");
        a["relevantIndicators"]!.AsArray().Count.ShouldBe(2);
        a["catalysts"]!.AsArray().Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("```json\n{0}\n```")]
    [InlineData("```\n{0}```")]
    [InlineData("  \n{0}\n ")]
    public async Task A_code_fence_around_the_output_is_stripped(string wrapper) =>
        _validator.Validate(wrapper.Replace("{0}", Valid(), StringComparison.Ordinal), await InputAsync()).Status.ShouldBe(ValidationStatus.Valid);

    [Fact]
    public async Task Invalid_json_is_invalid_and_repairable()
    {
        var result = _validator.Validate(FakeAiProvider.Fixture(FakeAiScenario.InvalidJson), await InputAsync());

        (result.Status, result.Repairable, result.Issues[0].Code).ShouldBe((ValidationStatus.Invalid, true, "parse_error"));
        result.Analysis.ShouldBeNull();
    }

    [Theory]
    [InlineData("{\"a\":1,}")] // trailing comma
    [InlineData("{/* c */\"a\":1}")] // comments
    [InlineData("[1,2]")] // not an object
    [InlineData("{\"a\":1,\"b\":{\"c\":1,\"c\":2}}")] // duplicate keys (seen from qwen3.5:0.8b in T-076)
    public async Task Strict_parsing_rejects_lenient_json(string raw) =>
        _validator.Validate(raw, await InputAsync()).Issues[0].Code.ShouldBe("parse_error");

    [Fact]
    public async Task Deep_or_huge_output_is_rejected()
    {
        var deep = string.Concat(Enumerable.Repeat("{\"a\":", 17)) + "1" + new string('}', 17);
        _validator.Validate(deep, await InputAsync()).Issues[0].Code.ShouldBe("parse_error");

        var huge = Edit(j => j["explanation"] = new string('x', AiOutputValidator.MaxBytes));
        var result = _validator.Validate(huge, await InputAsync());
        (result.Issues[0].Code, result.Repairable).ShouldBe(("parse_error", false));
    }

    [Fact]
    public async Task Schema_errors_are_invalid_and_repairable_with_paths()
    {
        var result = _validator.Validate(FakeAiProvider.Fixture(FakeAiScenario.SchemaError), await InputAsync());

        (result.Status, result.Repairable).ShouldBe((ValidationStatus.Invalid, true));
        result.Issues.ShouldAllBe(i => i.Code == "schema_error");
        result.Issues.ShouldContain(i => i.Path == "/overallSentiment");
        result.Issues.ShouldContain(i => i.Path == "/explanation");
    }

    [Theory]
    [InlineData("explanation", 2001)]
    [InlineData("list", 301)]
    [InlineData("items", 9)]
    [InlineData("price", 0)]
    [InlineData("confidence", 0)]
    public async Task Schema_bounds_are_enforced(string what, int size)
    {
        var raw = Edit(j =>
        {
            switch (what)
            {
                case "explanation": j["explanation"] = new string('x', size); break;
                case "list": j["keyRisks"] = new JsonArray(new string('x', size)); break;
                case "items": j["keyRisks"] = new JsonArray(Enumerable.Range(0, size).Select(i => (JsonNode)$"risk {i}").ToArray()); break;
                case "price": j["suggestedLevels"]!["stopLoss"] = "178.2.0"; break;
                case "confidence": j["confidence"]!["value"] = 1.5m; break;
            }
        });

        _validator.Validate(raw, await InputAsync()).Issues.ShouldContain(i => i.Code == "schema_error");
    }

    [Fact]
    public async Task Another_symbol_or_data_time_is_invalid_and_not_repaired()
    {
        var symbol = _validator.Validate(Edit(j => j["symbol"] = "AMD"), await InputAsync());
        (symbol.Status, symbol.Repairable, symbol.Issues[0].Code).ShouldBe((ValidationStatus.Invalid, false, "identity_mismatch"));

        var time = _validator.Validate(Edit(j => j["dataTimestamp"] = "2026-11-08T19:32:00Z"), await InputAsync());
        (time.Status, time.Issues[0].Path).ShouldBe((ValidationStatus.Invalid, "/dataTimestamp"));
    }

    [Fact]
    public async Task Data_that_aged_past_its_budget_marks_the_result_stale()
    {
        var result = _validator.Validate(Valid(), await InputAsync(fresh: false));

        (result.Status, result.Stale).ShouldBe((ValidationStatus.Valid, true));
        result.Analysis!["stale"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task Hallucinated_numbers_are_dropped_levels_rejected_and_forbidden_fields_removed()
    {
        var result = _validator.Validate(FakeAiProvider.Fixture(FakeAiScenario.HallucinatedNumbers), await InputAsync());

        result.Status.ShouldBe(ValidationStatus.ValidWithRejections);
        var a = result.Analysis!;
        a["relevantIndicators"]!.AsArray().ShouldBeEmpty();
        result.Issues.Where(i => i.Code == "ungrounded_indicator").Select(i => i.Message).ShouldBe(["relVolume20 is 8.00, but the context says 2.40.", "rsi14 is 91.0, but the context says 64.2."]);
        result.Issues.Where(i => i.Code == "forbidden_field").Select(i => i.Path).ShouldBe(["/rewardRisk", "/positionSize"]);
        a.ContainsKey("positionSize").ShouldBeFalse();
        a["rewardRisk"]!.GetValue<string>().ShouldBe("1.63"); // the backend's, (400 − 245) ÷ (245 − 150)
        result.Levels!.Accepted.ShouldBeFalse();
        result.Levels.Reasons.ShouldContain("Entry low 240.00 is outside the price band 149.12–223.68.");
        result.Levels.Reasons.ShouldContain("Target 400.00 is outside the price band 149.12–223.68.");
        result.Levels.Reasons.ShouldContain(r => r.StartsWith("Stop is too wide (17.18× ATR)", StringComparison.Ordinal));
        a["suggestedLevels"]!["accepted"]!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public async Task Indicators_within_half_a_percent_are_grounded_and_unknown_ones_dropped()
    {
        var raw = Edit(j => j["relevantIndicators"] = JsonNode.Parse("""[{"name":"rsi14","value":"64.5","interpretation":"ok"},{"name":"atr14","value":"5.60","interpretation":"too far"},{"name":"trend","value":"Uptrend","interpretation":"ok"},{"name":"pegRatio","value":"1.2","interpretation":"invented"}]"""));

        var result = _validator.Validate(raw, await InputAsync());

        result.Analysis!["relevantIndicators"]!.AsArray().Select(i => i!["name"]!.GetValue<string>()).ShouldBe(["rsi14", "trend"]);
        result.Issues.Select(i => i.Message).ShouldContain("pegRatio is not in the context.");
    }

    [Fact]
    public async Task Catalyst_dates_must_appear_in_the_context()
    {
        var raw = Edit(j => j["catalysts"] = JsonNode.Parse("""[{"type":"earnings","date":"2026-11-19","note":"ok"},{"type":"macro","date":"2026-12-10","note":"invented"},{"type":"other","date":null,"note":"undated"}]"""));

        var result = _validator.Validate(raw, await InputAsync());

        result.Analysis!["catalysts"]!.AsArray().Count.ShouldBe(2);
        result.Issues.Single(i => i.Code == "ungrounded_catalyst").Message.ShouldBe("Catalyst date 2026-12-10 is not in the context.");
    }

    [Theory]
    [InlineData("This breakout is guaranteed to work.")]
    [InlineData("A risk-free entry.")]
    [InlineData("You can't lose here")]
    [InlineData("I am 100% sure")]
    public async Task Guarantee_language_is_replaced(string text)
    {
        var raw = Edit(j => j["supportingEvidence"]![0] = text);

        var result = _validator.Validate(raw, await InputAsync());

        result.Status.ShouldBe(ValidationStatus.ValidWithRejections);
        result.Analysis!["supportingEvidence"]![0]!.GetValue<string>().ShouldBe(ContentPolicy.Fallback);
        result.Issues.Single().Path.ShouldBe("/supportingEvidence/0");
    }

    [Fact]
    public async Task Unjustified_levels_are_not_accepted_but_are_not_a_rejection()
    {
        var raw = Edit(j => j["suggestedLevels"] = JsonNode.Parse("""{"justified":false,"entryZone":null,"stopLoss":null,"takeProfit":null,"rationale":null}"""));

        var result = _validator.Validate(raw, await InputAsync());

        result.Status.ShouldBe(ValidationStatus.Valid);
        result.Levels!.Accepted.ShouldBeFalse();
        result.Analysis!["rewardRisk"].ShouldBeNull();
    }

    [Fact]
    public async Task Reward_risk_below_the_users_minimum_rejects_the_levels()
    {
        var result = _validator.Validate(Valid(), await InputAsync(minRewardRisk: 2m));

        result.Levels!.Reasons.ShouldBe(["Reward/risk 1.77 is below the minimum 2.0."]);
        result.Status.ShouldBe(ValidationStatus.ValidWithRejections);
    }

    [Fact]
    public void Level_validator_matches_the_risk_example_R2()
    {
        var result = LevelValidator.Validate(new SuggestedLevels(186.50m, 186.50m, 185.00m, 204.00m), 149.12m, 223.68m, 5.53m, 1.5m);

        result.Accepted.ShouldBeFalse();
        result.Reasons.ShouldBe(["Stop is too close (0.27× ATR). Minimum 0.5× ATR."]);
    }

    [Fact]
    public void Level_validator_checks_order_and_zone_width()
    {
        var inverted = LevelValidator.Validate(new SuggestedLevels(186m, 187m, 190m, 204m), 149.12m, 223.68m, 5.53m, 1.5m);
        inverted.Reasons[0].ShouldStartWith("Levels must satisfy stop < entry zone < target");
        inverted.RewardRisk.ShouldBeNull();

        var wide = LevelValidator.Validate(new SuggestedLevels(180m, 186.5m, 172m, 220m), 149.12m, 223.68m, 5.53m, 1.5m);
        wide.Reasons.ShouldBe(["Entry zone is wider than 1.0× ATR (6.50 vs 5.53)."]);
    }

    private static (AnalysisGenerator Generator, FakeAiProvider Fake) Generator()
    {
        var fake = new FakeAiProvider();
        var generator = new AnalysisGenerator(fake, new AiOutputValidator(), new AlwaysFresh(), new FakeTimeProvider(Now), Options.Create(new AiGenerationOptions()));
        return (generator, fake);
    }

    private sealed class AlwaysFresh : IMarketDataFreshness
    {
        public Task<MarketFreshness> GetAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> IsQuoteFreshAsync(DateTimeOffset asOf, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    [Theory]
    [InlineData(FakeAiScenario.InvalidJson, "Your previous output was not valid JSON: ")]
    [InlineData(FakeAiScenario.SchemaError, "Your previous output did not match the schema: ")]
    public async Task One_repair_retry_carries_the_error_back_to_the_model(FakeAiScenario first, string prompt)
    {
        var (generator, fake) = Generator();
        fake.Enqueue(first, FakeAiScenario.Valid);

        var result = await generator.GenerateAsync(await new Stubs().BuildAsync(), Prompt, 1.5m, default);

        result.Validation.Status.ShouldBe(ValidationStatus.Valid);
        result.Attempts.Count.ShouldBe(2);
        var repair = fake.Requests.ToArray()[1].Messages;
        repair.Select(m => m.Role).ShouldBe(["system", "user", "assistant", "user"]);
        repair[3].Content.ShouldStartWith(prompt);
        repair[3].Content.ShouldEndWith("Reply with only the corrected JSON object.");
    }

    [Fact]
    public async Task The_prompts_output_example_travels_with_the_request()
    {
        var (generator, fake) = Generator();
        var next = Stubs.Registry().Get(PromptRegistry.StockAnalysis, "2026-11-12.1");

        await generator.GenerateAsync(await new Stubs().BuildAsync(), next, 1.5m, default);

        fake.Requests.Single().OutputExample.ShouldBe(next.OutputExample);
    }

    [Fact]
    public async Task A_second_bad_output_is_final()
    {
        var (generator, fake) = Generator();
        fake.Enqueue(FakeAiScenario.InvalidJson, FakeAiScenario.InvalidJson, FakeAiScenario.Valid);

        var result = await generator.GenerateAsync(await new Stubs().BuildAsync(), Prompt, 1.5m, default);

        (result.Validation.Status, result.Attempts.Count, fake.Requests.Count).ShouldBe((ValidationStatus.Invalid, 2, 2));
    }

    [Fact]
    public async Task Rejections_are_not_retried_and_provider_failures_propagate()
    {
        var (generator, fake) = Generator();
        fake.Enqueue(FakeAiScenario.HallucinatedNumbers, FakeAiScenario.ServerError);
        var context = await new Stubs().BuildAsync();

        (await generator.GenerateAsync(context, Prompt, 1.5m, default)).Attempts.Count.ShouldBe(1);
        await Should.ThrowAsync<AiProviderUnavailableException>(() => generator.GenerateAsync(context, Prompt, 1.5m, default));
    }
}
