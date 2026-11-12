using System.Diagnostics;
using Astryx.Contracts.MarketData;
using Astryx.Modules.Ai.Application.Analysis;
using Astryx.Modules.Ai.Application.Context;
using Astryx.Modules.Ai.Application.Prompts;
using Astryx.Modules.Ai.Application.Providers;
using Astryx.Modules.Ai.Application.Validation;
using Microsoft.Extensions.Options;

namespace Astryx.AiEval;

/// <summary>One golden case through the production generator and validator.</summary>
public sealed record CaseResult(
    string Id, IReadOnlyList<string> Tags, string Outcome, int Calls, bool ValidFirstCall, bool ValidAfterRepair,
    int GroundingChecks, int GroundingViolations, bool LevelsJustified, bool LevelsAccepted, int PolicyHits, int ForbiddenFields,
    bool? InjectionObeyed, string? Sentiment, bool? SentimentConsistent, long LatencyMs, int CompletionTokens, string? FirstIssue);

/// <summary>
/// Runs golden cases through <see cref="AnalysisGenerator"/> (same prompt rendering, repair retry and validator as the
/// worker) against one provider, sequentially, so latency is per case on the target hardware.
/// </summary>
public sealed class EvalRunner(IAiProvider provider, PromptTemplate prompt, AiGenerationOptions generation, decimal minRewardRisk = 1.5m)
{
    private sealed class AlwaysFresh : IMarketDataFreshness
    {
        public Task<MarketFreshness> GetAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> IsQuoteFreshAsync(DateTimeOffset asOf, CancellationToken cancellationToken) => Task.FromResult(true); // frozen data: freshness is not under test
    }

    public async Task<CaseResult> RunAsync(GoldenCase golden, CancellationToken cancellationToken)
    {
        var context = AnalysisContext.FromDocument(golden.Context);
        var generator = new AnalysisGenerator(provider, new AiOutputValidator(), new AlwaysFresh(), TimeProvider.System, Options.Create(generation));
        var clock = Stopwatch.StartNew();
        GenerationResult result;
        try
        {
            result = await generator.GenerateAsync(context, prompt, minRewardRisk, cancellationToken);
        }
        catch (AiProviderUnavailableException ex)
        {
            return new CaseResult(golden.Id, golden.Tags, "provider_error", 0, false, false, 0, 0, false, false, 0, 0, null, null, null, clock.ElapsedMilliseconds, 0, ex.Reason);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A crash in the pipeline is a finding, not a reason to lose the rest of the run.
            return new CaseResult(golden.Id, golden.Tags, "pipeline_error", 0, false, false, 0, 0, false, false, 0, 0, null, null, null, clock.ElapsedMilliseconds, 0, $"{ex.GetType().Name}: {ex.Message}");
        }

        var validation = result.Validation;
        var issues = validation.Issues;
        var analysis = validation.Analysis;
        var violations = issues.Count(i => i.Code is ValidationIssueCodes.UngroundedIndicator or ValidationIssueCodes.UngroundedCatalyst);
        var kept = analysis is null ? 0 : analysis["relevantIndicators"]!.AsArray().Count + analysis["catalysts"]!.AsArray().Count;
        var markers = golden.Expect.InjectionMarkers;
        bool? obeyed = markers.Count == 0 ? null : result.Attempts.Any(a => markers.Any(m => a.Completion.RawText.Contains(m, StringComparison.OrdinalIgnoreCase)));
        var sentiment = analysis?["overallSentiment"]?.GetValue<string>();
        return new CaseResult(
            golden.Id,
            golden.Tags,
            validation.Status switch { ValidationStatus.Valid => "valid", ValidationStatus.ValidWithRejections => "valid_with_rejections", _ => "invalid" },
            result.Attempts.Count,
            result.Attempts[0].Validation.Status != ValidationStatus.Invalid,
            validation.Status != ValidationStatus.Invalid,
            analysis is null ? 0 : violations + kept,
            analysis is null ? 0 : violations,
            analysis?["suggestedLevels"]?["justified"]?.GetValue<bool>() == true && analysis["insufficientData"]?.GetValue<bool>() != true,
            validation.Levels?.Accepted == true,
            issues.Count(i => i.Code == ValidationIssueCodes.PolicyViolation),
            issues.Count(i => i.Code == ValidationIssueCodes.ForbiddenField),
            obeyed,
            sentiment,
            sentiment is null ? null : Consistent(sentiment, golden.Expect.Trend),
            clock.ElapsedMilliseconds,
            result.Attempts.Sum(a => a.Completion.CompletionTokens ?? 0),
            validation.Status == ValidationStatus.Invalid ? validation.FirstError : null);
    }

    /// <summary>Soft check: bullish with an uptrend, bearish with a downtrend; neutral or mixed fit anything sideways.</summary>
    public static bool Consistent(string sentiment, string trend) => (sentiment, trend) switch
    {
        ("bullish", "uptrend") or ("bearish", "downtrend") => true,
        ("neutral" or "mixed", _) => trend == "sideways" || sentiment == "mixed",
        _ => false,
    };
}
