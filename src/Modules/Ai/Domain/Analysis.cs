using Astryx.Infrastructure.Security;

namespace Astryx.Modules.Ai.Domain;

public static class AnalysisKind
{
    public const string StockAnalysis = "stock_analysis";
    public const string SignalExplanation = "signal_explanation";
    public const string NewsSummary = "news_summary";
}

public static class AnalysisJobStatus
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";

    public static bool IsTerminal(string status) => status is Completed or Failed or Cancelled;
}

/// <summary>Job error codes (SYSTEM_DESIGN.md §10.4 <c>error_code</c>). The detail shown with them never includes prompts.</summary>
public static class AnalysisErrorCodes
{
    public const string ProviderUnavailable = "ai_provider_unavailable";
    public const string Timeout = "ai_timeout";
    public const string OutputInvalid = "ai_output_invalid";
    public const string DataStale = "data_stale";
    public const string InsufficientHistory = "insufficient_history";
    public const string InstrumentNotFound = "instrument_not_found";
    public const string Cancelled = "cancelled";
    public const string LeaseExpired = "lease_expired";
}

/// <summary>Row in <c>ai.analysis_jobs</c> (§10.4): one requested analysis and its lease.</summary>
public sealed class AnalysisJob : IUserOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid InstrumentId { get; set; }

    public required string Symbol { get; set; }

    public Guid? SignalId { get; set; }

    public string Kind { get; set; } = AnalysisKind.StockAnalysis;

    public string Timeframe { get; set; } = "1d";

    public string Status { get; set; } = AnalysisJobStatus.Pending;

    public required string IdempotencyKey { get; set; }

    /// <summary>Hash of (instrument, kind, timeframe, 15-minute data bucket): equal hashes are the same question.</summary>
    public required string RequestHash { get; set; }

    public int Attempt { get; set; }

    public int MaxAttempts { get; set; } = 3;

    public string? LockedBy { get; set; }

    public DateTimeOffset? LockedUntil { get; set; }

    /// <summary>Not before this time: the retry backoff (10 s, 30 s, 90 s + jitter).</summary>
    public DateTimeOffset RunAfter { get; set; }

    /// <summary>Set by AI3 while processing; the worker stops at its next checkpoint.</summary>
    public DateTimeOffset? CancelRequestedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorDetail { get; set; }

    public Guid? ResultId { get; set; }
}

public static class ValidationStatusNames
{
    public const string Valid = "valid";
    public const string ValidWithRejections = "valid_with_rejections";
    public const string Invalid = "invalid";
}

/// <summary>
/// Row in <c>ai.analysis_results</c> (§10.4): the exact context, the verbatim output and the validated analysis.
/// An invalid output is kept for evaluation (<c>parsed</c> null) but never linked from its job.
/// </summary>
public sealed class AnalysisResult : IUserOwned
{
    public const int MaxRawOutput = 64 * 1024;

    public Guid Id { get; set; }

    public Guid JobId { get; set; }

    public Guid UserId { get; set; }

    public Guid InstrumentId { get; set; }

    public required string Symbol { get; set; }

    public required string Provider { get; set; }

    public required string Model { get; set; }

    public string? ModelDigest { get; set; }

    public required string PromptId { get; set; }

    public required string PromptVersion { get; set; }

    public required string OutputSchemaVersion { get; set; }

    public required string Context { get; set; }

    public required string RawOutput { get; set; }

    public string? Parsed { get; set; }

    public required string ValidationStatus { get; set; }

    public required string ValidationIssues { get; set; }

    public string? Sentiment { get; set; }

    public decimal? Confidence { get; set; }

    public DateTimeOffset DataAsOf { get; set; }

    public DateTimeOffset AnalysisAt { get; set; }

    public string? LevelValidation { get; set; }

    public decimal? RewardRisk { get; set; }

    public bool Stale { get; set; }

    public int? PromptTokens { get; set; }

    public int? CompletionTokens { get; set; }

    public int LatencyMs { get; set; }
}

public static class AiRequestOutcome
{
    public const string Ok = "ok";
    public const string Timeout = "timeout";
    public const string HttpError = "http_error";
    public const string ParseError = "parse_error";
    public const string SchemaError = "schema_error";
    public const string Cancelled = "cancelled";
}

/// <summary>Row in <c>ai.ai_requests</c>: one provider call. No prompt or output text (§12.9).</summary>
public sealed class AiRequest
{
    public Guid Id { get; set; }

    public Guid JobId { get; set; }

    public int Attempt { get; set; }

    public required string Provider { get; set; }

    public required string Model { get; set; }

    public required string PromptVersion { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public int DurationMs { get; set; }

    public int? PromptTokens { get; set; }

    public int? CompletionTokens { get; set; }

    public required string Outcome { get; set; }

    public int? HttpStatus { get; set; }

    public string? ErrorMessage { get; set; }
}
