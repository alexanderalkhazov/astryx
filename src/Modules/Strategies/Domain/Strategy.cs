namespace Astryx.Modules.Strategies.Domain;

public static class StrategyStatus
{
    public const string Active = "active";

    /// <summary>The drawdown guard is warning (design "Active · guard").</summary>
    public const string ActiveGuarded = "active_guarded";

    public const string Paused = "paused";
    public const string Retired = "retired";
}

/// <summary>Row in <c>strategy.strategies</c> (SYSTEM_DESIGN.md §10.4). System strategies have no owner.</summary>
public sealed class Strategy
{
    public Guid Id { get; set; }

    /// <summary>Stable key, e.g. <c>base_breakout</c>.</summary>
    public required string Code { get; set; }

    public required string Name { get; set; }

    public required string Description { get; set; }

    /// <summary>JSON array of rule sentences shown on the Overview tab.</summary>
    public required string Rules { get; set; }

    /// <summary>JSON array of parameter definitions (key, label, unit, default, min, max, step); versions are validated against it.</summary>
    public required string ParamSchema { get; set; }

    public required string Status { get; set; }

    /// <summary>Null for system strategies; per-user strategies are Q-10.</summary>
    public Guid? OwnerUserId { get; set; }

    public Guid? PublishedVersionId { get; set; }

    public DateOnly? LiveSince { get; set; }

    /// <summary>
    /// Illustrative live paper results (since inception, month to date) until Portfolio attribution exists (M5).
    /// JSON; every response built from it says <c>illustrative: true</c>.
    /// </summary>
    public string? IllustrativeLive { get; set; }

    /// <summary>Order in the Strategy lab and the colour index in charts.</summary>
    public int DisplayOrder { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public static class VersionState
{
    public const string Draft = "draft";
    public const string Published = "published";
    public const string Archived = "archived";
}

/// <summary>Row in <c>strategy.strategy_versions</c>. Published and archived params never change (a trigger enforces it).</summary>
public sealed class StrategyVersion
{
    public Guid Id { get; set; }

    public Guid StrategyId { get; set; }

    public int VersionNumber { get; set; }

    public required string State { get; set; }

    /// <summary>JSON object: parameter key → value (numbers, or decimal strings for fractional values).</summary>
    public required string Params { get; set; }

    /// <summary>Rule implementation version, e.g. <c>base_breakout@1</c>.</summary>
    public required string CodeVersion { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public static class RunStatus
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

/// <summary>Row in <c>strategy.backtest_runs</c>. MVP runs are seeded and illustrative (§7.8).</summary>
public sealed class BacktestRun
{
    public Guid Id { get; set; }

    public Guid StrategyId { get; set; }

    public Guid StrategyVersionId { get; set; }

    public int RunNumber { get; set; }

    public required string Status { get; set; }

    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }

    public DateOnly OosStart { get; set; }

    public decimal InitialCapital { get; set; }

    /// <summary>JSON: fees, slippage, universe, survivorship note, benchmark.</summary>
    public required string Assumptions { get; set; }

    /// <summary>JSON: <c>full</c>, <c>inSample</c>, <c>outOfSample</c> blocks of decimal strings.</summary>
    public required string Metrics { get; set; }

    /// <summary>JSON array of <c>[date, equity]</c> strings, at most 1000 points.</summary>
    public required string EquityCurve { get; set; }

    public required string DrawdownCurve { get; set; }

    public required string MonthlyReturns { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public string? ErrorCode { get; set; }
}

/// <summary>Row in <c>strategy.backtest_trades</c>; filled by the backtest engine (Q-10), empty for seeded runs.</summary>
public sealed class BacktestTrade
{
    public Guid Id { get; set; }

    public Guid RunId { get; set; }

    /// <summary>Refers to <c>market.instruments</c> (no cross-module foreign key, §9.2).</summary>
    public Guid InstrumentId { get; set; }

    public required string Segment { get; set; }

    public DateOnly EntryDate { get; set; }

    public DateOnly ExitDate { get; set; }

    public decimal EntryPrice { get; set; }

    public decimal ExitPrice { get; set; }

    public decimal Quantity { get; set; }

    public decimal Pnl { get; set; }

    public decimal RMultiple { get; set; }

    public required string ExitReason { get; set; }
}
