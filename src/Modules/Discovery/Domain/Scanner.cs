namespace Astryx.Modules.Discovery.Domain;

public static class ScannerRunStatus
{
    public const string Running = "running";
    public const string Completed = "completed";
    public const string CompletedWithWarnings = "completed_with_warnings";
    public const string Failed = "failed";
}

/// <summary>Row in <c>discovery.scanner_runs</c> (SYSTEM_DESIGN.md §10.4).</summary>
public sealed class ScannerRun
{
    public Guid Id { get; set; }

    /// <summary><c>schedule</c>, <c>manual</c> or <c>eod</c>.</summary>
    public required string Trigger { get; set; }

    public required string Status { get; set; }

    public required string UniverseCode { get; set; }

    public int SymbolsTotal { get; set; }

    public int SymbolsEvaluated { get; set; }

    /// <summary>Symbols that could not be evaluated (too little or stale history, errors); they make the run complete with warnings.</summary>
    public int SymbolsSkipped { get; set; }

    /// <summary>Oldest last-bar time among evaluated symbols.</summary>
    public DateTimeOffset? DataAsOf { get; set; }

    /// <summary>Market data markers at the start of the run; the next run is due when either moves on.</summary>
    public DateTimeOffset? QuotesAsOf { get; set; }

    public DateOnly? BarsThrough { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>e.g. <c>SCN-503</c> (no usable market data) or <c>SCN-500</c> (unexpected).</summary>
    public string? ErrorCode { get; set; }
}

public static class SignalStatus
{
    public const string Active = "active";

    /// <summary>A newer run produced a signal for the same instrument and strategy.</summary>
    public const string Superseded = "superseded";

    /// <summary>A newer run no longer finds the setup, or the signal outlived <c>expires_at</c>.</summary>
    public const string Expired = "expired";

    public const string Invalidated = "invalidated";
}

/// <summary>
/// Row in <c>discovery.signals</c>: global scanner output, not user-scoped (§10.4). Rejected and expired signals are
/// kept for hit-rate audits.
/// </summary>
public sealed class Signal
{
    public Guid Id { get; set; }

    public Guid ScannerRunId { get; set; }

    /// <summary>Refers to <c>market.instruments</c> (no cross-module foreign key, §9.2).</summary>
    public Guid InstrumentId { get; set; }

    public required string Symbol { get; set; }

    /// <summary>Refers to <c>strategy.strategy_versions</c> (no cross-module foreign key).</summary>
    public Guid StrategyVersionId { get; set; }

    public Guid StrategyId { get; set; }

    public required string StrategyName { get; set; }

    public required string SignalType { get; set; }

    public required string Horizon { get; set; }

    public decimal Score { get; set; }

    /// <summary>JSON <c>[{component, weight, value, contribution}]</c>.</summary>
    public required string ScoreBreakdown { get; set; }

    public required string Reason { get; set; }

    public required string OrderType { get; set; }

    public decimal EntryPrice { get; set; }

    public decimal StopPrice { get; set; }

    public decimal TargetPrice { get; set; }

    /// <summary>JSON of decimal strings: <c>relVolume</c>, <c>relStrength</c>, <c>rsi14</c>, <c>rewardRisk</c>, <c>atr14</c>.</summary>
    public required string Metrics { get; set; }

    public required string Status { get; set; }

    public DateTimeOffset EvaluatedAt { get; set; }

    public DateTimeOffset DataAsOf { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>Row in <c>discovery.universe_members</c>: who was in a universe and when (§10.4).</summary>
public sealed class UniverseMember
{
    public required string UniverseCode { get; set; }

    public Guid InstrumentId { get; set; }

    public DateTimeOffset AddedAt { get; set; }

    public DateTimeOffset? RemovedAt { get; set; }
}
