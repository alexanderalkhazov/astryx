using System.Text.Json.Nodes;
using Astryx.AiEval;
using Astryx.Modules.Ai.Application.Analysis;
using Astryx.Modules.Ai.Application.Context;
using Astryx.Modules.Ai.Application.Prompts;
using Astryx.Modules.Ai.Application.Providers;
using Astryx.Modules.Ai.Application.Validation;

namespace Astryx.UnitTests.Ai;

/// <summary>The golden evaluation set and the AiEval harness (SYSTEM_DESIGN.md §12.10, TASKS.md T-076).</summary>
public class AiEvalTests
{
    private static readonly string GoldenDir = Path.Combine(RepoRoot(), "tests/fixtures/ai/golden");
    private static readonly IReadOnlyList<GoldenCase> Cases = GoldenCase.LoadAll(GoldenDir);
    private static readonly PromptTemplate Prompt = Stubs.Registry().GetActive(PromptRegistry.StockAnalysis);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir!.FullName, "Astryx.slnx")))
        {
            dir = dir.Parent;
        }

        return dir.FullName;
    }

    /// <summary>A fully grounded answer built from the context itself: what a perfect model would say.</summary>
    private static string GroundedAnswer(JsonObject ctx)
    {
        var indicators = ctx["indicators"]!.AsObject();
        var earnings = ctx["fundamentals"]?["nextEarningsDate"]?.GetValue<string>();
        var answer = new JsonObject
        {
            ["schemaVersion"] = "analysis.v1",
            ["symbol"] = ctx["symbol"]!.GetValue<string>(),
            ["analysisTimestamp"] = ctx["dataAsOf"]!.GetValue<string>(),
            ["dataTimestamp"] = ctx["dataAsOf"]!.GetValue<string>(),
            ["dataQuality"] = new JsonObject { ["status"] = ctx["dataQuality"]!["status"]!.GetValue<string>(), ["notes"] = new JsonArray() },
            ["overallSentiment"] = "neutral",
            ["scenarios"] = JsonNode.Parse("""{"bullish":{"summary":"Up.","conditions":["Holds support"]},"bearish":{"summary":"Down.","conditions":["Breaks support"]},"neutral":{"summary":"Flat.","conditions":["Range"]}}"""),
            ["supportingEvidence"] = new JsonArray("Trend is " + indicators["trend"]!.GetValue<string>()),
            ["contradictingEvidence"] = new JsonArray(),
            ["keyRisks"] = new JsonArray("Volatility"),
            ["relevantIndicators"] = new JsonArray(
                new JsonObject { ["name"] = "rsi14", ["value"] = indicators["rsi14"]!.GetValue<string>(), ["interpretation"] = "As reported" },
                new JsonObject { ["name"] = "atr14", ["value"] = indicators["atr14"]!.GetValue<string>(), ["interpretation"] = "As reported" }),
            ["catalysts"] = earnings is null ? new JsonArray() : new JsonArray(new JsonObject { ["type"] = "earnings", ["date"] = earnings, ["note"] = "Earnings" }),
            ["confidence"] = new JsonObject { ["value"] = 0.4m, ["explanation"] = "Subjective, not a probability." },
            ["suggestedLevels"] = JsonNode.Parse("""{"justified":false,"entryZone":null,"stopLoss":null,"takeProfit":null,"rationale":null}"""),
            ["explanation"] = "Grounded test answer.",
            ["insufficientData"] = false,
        };
        return answer.ToJsonString();
    }

    [Fact]
    public void The_golden_set_has_at_least_fifty_varied_cases()
    {
        Cases.Count.ShouldBeGreaterThanOrEqualTo(50);
        Cases.Select(c => c.Id).Distinct().Count().ShouldBe(Cases.Count);
        foreach (var tag in new[] { "uptrend", "downtrend", "sideways", "earnings", "sparse", "injection", "signal" })
        {
            Cases.Count(c => c.Tags.Contains(tag)).ShouldBeGreaterThanOrEqualTo(3, tag);
        }
    }

    [Fact]
    public void Every_injection_case_plants_its_markers_in_the_news_only()
    {
        foreach (var c in Cases.Where(c => c.Tags.Contains("injection")))
        {
            c.Expect.InjectionMarkers.Count.ShouldBe(3);
            var news = c.Context["news"]!.ToJsonString();
            var rest = c.Context.DeepClone().AsObject();
            rest.Remove("news");
            c.Expect.InjectionMarkers.ShouldAllBe(m => news.Contains(m, StringComparison.Ordinal) && !rest.ToJsonString().Contains(m, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Every_golden_context_rehydrates_renders_and_accepts_a_grounded_answer()
    {
        var validator = new AiOutputValidator();
        foreach (var c in Cases)
        {
            var context = AnalysisContext.FromDocument(c.Context);
            var last = decimal.Parse(c.Context["quote"]!["last"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture);
            (context.PriceBandMin, context.PriceBandMax).ShouldBe((Math.Round(last * 0.8m, 2, MidpointRounding.ToEven), Math.Round(last * 1.2m, 2, MidpointRounding.ToEven)), c.Id);
            Prompt.Render(context)[1].Content.ShouldContain($"symbol \"{c.Id}\"");

            var result = validator.Validate(GroundedAnswer(c.Context), new ValidationInput(context, Prompt, "test", DateTimeOffset.UtcNow, true, 1.5m));

            result.Status.ShouldBe(ValidationStatus.Valid, $"{c.Id}: {string.Join("; ", result.Issues.Select(i => i.Message))}");
        }
    }

    private sealed class Scripted(Func<AiCompletionRequest, string> answer) : IAiProvider
    {
        public string Name => "scripted";

        public Task<AiModelInfo> GetModelInfoAsync(CancellationToken cancellationToken) => Task.FromResult(new AiModelInfo("scripted", "d", 1, "0B", "q"));

        public Task<AiCompletion> CompleteJsonAsync(AiCompletionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new AiCompletion(answer(request), 10, 20, TimeSpan.FromMilliseconds(5), "scripted", "d", "stop"));
    }

    [Fact]
    public async Task The_runner_scores_grounding_and_detects_an_obeyed_injection()
    {
        var injected = Cases.First(c => c.Tags.Contains("injection"));
        var obeying = JsonNode.Parse(GroundedAnswer(injected.Context))!.AsObject();
        obeying["keyRisks"] = new JsonArray($"A stop near {injected.Expect.InjectionMarkers[0]}");
        var runner = new EvalRunner(new Scripted(_ => obeying.ToJsonString()), Prompt, new AiGenerationOptions());

        var result = await runner.RunAsync(injected, default);

        (result.Outcome, result.Calls, result.InjectionObeyed, result.GroundingViolations).ShouldBe(("valid", 1, true, 0));
        result.GroundingChecks.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Garbage_is_retried_once_then_counted_invalid_and_the_gate_fails()
    {
        var calls = 0;
        var runner = new EvalRunner(new Scripted(_ => ++calls % 2 == 1 ? "not json" : "{still not"), Prompt, new AiGenerationOptions());
        var results = new List<CaseResult>();
        foreach (var c in Cases.Take(3))
        {
            results.Add(await runner.RunAsync(c, default));
        }

        var report = EvalReport.Build("scripted", "stock_analysis@2026-11-09.1", "scripted", DateTimeOffset.UtcNow, results);

        results.ShouldAllBe(r => r.Outcome == "invalid" && r.Calls == 2);
        report.SchemaValidAfterRepair.ShouldBe(new Rate(0, 3));
        report.GatePassed.ShouldBeFalse();
        report.ToMarkdown().ShouldContain("## Promotion gate: FAIL");
    }

    [Theory]
    [InlineData(new long[] { 10, 20, 30, 40 }, 50, 20)]
    [InlineData(new long[] { 10, 20, 30, 40 }, 95, 40)]
    [InlineData(new long[] { 7 }, 95, 7)]
    public void Percentiles_use_nearest_rank(long[] sorted, int percentile, long expected) =>
        EvalReport.Percentile(sorted, percentile).ShouldBe(expected);

    [Theory]
    [InlineData("bullish", "uptrend", true)]
    [InlineData("bullish", "downtrend", false)]
    [InlineData("neutral", "sideways", true)]
    [InlineData("neutral", "uptrend", false)]
    [InlineData("mixed", "downtrend", true)]
    public void Sentiment_consistency_is_soft(string sentiment, string trend, bool expected) =>
        EvalRunner.Consistent(sentiment, trend).ShouldBe(expected);
}
