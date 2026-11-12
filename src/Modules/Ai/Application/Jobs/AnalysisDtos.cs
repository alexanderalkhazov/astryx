namespace Astryx.Modules.Ai.Application.Jobs;

/// <summary>AI1 body.</summary>
/// <param name="Kind"><c>stock_analysis</c> (default). <c>signal_explanation</c> has no prompt yet.</param>
public sealed record CreateAnalysisRequest(string Symbol, string? Kind, Guid? SignalId, string? Timeframe);

/// <summary>AI2 (and the AI1 response). <c>pollAfterMs</c> is set while the job is still running.</summary>
public sealed record AnalysisJobDto(
    Guid Id, string Symbol, string Kind, string Status, int Attempt, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt,
    Guid? ResultId, string? ErrorCode, string? ErrorDetail, int? PollAfterMs);

public sealed record ScenarioDto(string Summary, IReadOnlyList<string> Conditions);

public sealed record ScenariosDto(ScenarioDto Bullish, ScenarioDto Bearish, ScenarioDto Neutral);

public sealed record IndicatorNoteDto(string Name, string Value, string Interpretation);

public sealed record CatalystDto(string Type, string? Date, string Note);

/// <summary>A subjective model rating. NOT a calibrated probability.</summary>
public sealed record ConfidenceDto(decimal Value, string Explanation);

public sealed record PriceZoneDto(string? Low, string? High);

/// <summary>The model's levels. <c>accepted</c> is the backend's verdict (LevelValidator).</summary>
public sealed record SuggestedLevelsDto(bool Justified, PriceZoneDto? EntryZone, string? StopLoss, string? TakeProfit, string? Rationale, bool Accepted);

public sealed record LevelValidationDto(bool Accepted, IReadOnlyList<string> Reasons);

public sealed record AnalysisDataQualityDto(string Status, IReadOnlyList<string>? Notes);

/// <summary>
/// AI4: the validated <c>analysis.v1</c> (SYSTEM_DESIGN.md §12.6) with the backend's fields. <c>rewardRisk</c> is
/// computed by the backend; <c>stale</c> also turns true 24 h after the analysis.
/// </summary>
public sealed record AnalysisResultDto(
    Guid Id, Guid JobId, string SchemaVersion, string Symbol, DateTimeOffset AnalysisTimestamp, DateTimeOffset DataTimestamp,
    AnalysisDataQualityDto DataQuality, string OverallSentiment, ScenariosDto Scenarios,
    IReadOnlyList<string> SupportingEvidence, IReadOnlyList<string> ContradictingEvidence, IReadOnlyList<string> KeyRisks,
    IReadOnlyList<IndicatorNoteDto> RelevantIndicators, IReadOnlyList<CatalystDto> Catalysts, ConfidenceDto Confidence,
    SuggestedLevelsDto SuggestedLevels, string Explanation, bool InsufficientData, string? RewardRisk, LevelValidationDto LevelValidation,
    bool Stale, string Model, string PromptVersion, string Disclaimer, string ValidationStatus);

/// <summary>AI5 row.</summary>
public sealed record AnalysisSummaryDto(
    Guid Id, Guid JobId, string Symbol, string? OverallSentiment, decimal? Confidence, DateTimeOffset AnalysisAt, DateTimeOffset DataAsOf,
    bool Stale, bool LevelsAccepted, decimal? RewardRisk);

public sealed record AnalysisPageDto(IReadOnlyList<AnalysisSummaryDto> Items, string? NextCursor);
