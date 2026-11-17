using Astryx.Infrastructure.Security;

namespace Astryx.Modules.Portfolio.Domain;

public static class PortfolioMode
{
    public const string Simulated = "simulated";
    public const string AlpacaPaper = "alpaca_paper";
    public const string AlpacaLive = "alpaca_live";
}

/// <summary>Row in <c>portfolio.portfolios</c> (SYSTEM_DESIGN.md §10.4). Cash is the sum of its ledger, cached here.</summary>
public sealed class Portfolio : IUserOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public required string Mode { get; set; }

    public Guid? BrokerConnectionId { get; set; }

    public string BaseCurrency { get; set; } = "USD";

    public decimal StartingCash { get; set; }

    public decimal CashBalance { get; set; }

    public decimal ReservedCash { get; set; }

    public decimal RealizedPnl { get; set; }

    public decimal? StartOfDayEquity { get; set; }

    public DateOnly? StartOfDayDate { get; set; }

    public int ResetCount { get; set; }

    /// <summary>Performance (P5), trade history and snapshots count from here; a reset starts a new epoch (§15.6).</summary>
    public DateTimeOffset EpochStartedAt { get; set; }

    /// <summary>Nightly reconciliation (§20.13) last checked this portfolio.</summary>
    public DateTimeOffset? ReconciledAt { get; set; }

    /// <summary>Set by a reconciliation mismatch: new entries are blocked, exits allowed. Cleared by an operator.</summary>
    public DateTimeOffset? ReconciliationHaltedAt { get; set; }

    /// <summary>JSON array of the mismatches that halted the portfolio.</summary>
    public string? ReconciliationIssues { get; set; }

    public long Version { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public static class LedgerType
{
    public const string InitialDeposit = "initial_deposit";
    public const string Buy = "buy";
    public const string Sell = "sell";
    public const string Fee = "fee";
    public const string ResetClose = "reset_close";
    public const string ResetDeposit = "reset_deposit";
    public const string Adjustment = "adjustment";
}

/// <summary>Row in <c>portfolio.transactions</c>: one append-only cash movement. <see cref="IdempotencyRef"/> is unique.</summary>
public sealed class LedgerTransaction
{
    public Guid Id { get; set; }

    public Guid PortfolioId { get; set; }

    /// <summary>Database identity: the order entries were posted in (ids and timestamps can tie within one posting).</summary>
    public long Seq { get; set; }

    public required string Type { get; set; }

    public decimal Amount { get; set; }

    public string Currency { get; set; } = "USD";

    public decimal BalanceAfter { get; set; }

    public Guid? FillId { get; set; }

    public Guid? OrderId { get; set; }

    public Guid? PositionId { get; set; }

    public required string IdempotencyRef { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public string? Description { get; set; }
}

public static class PositionStatus
{
    public const string Open = "open";
    public const string Closed = "closed";
}

public static class ExitReason
{
    public const string Stop = "stop";
    public const string Target = "target";
    public const string Manual = "manual";
    public const string Reset = "reset";
}

/// <summary>
/// Row in <c>portfolio.positions</c> (§10.4): at most one open position per instrument. Order, proposal and strategy
/// ids point into other schemas, so they carry no foreign keys (as in <c>discovery</c>).
/// </summary>
public sealed class Position
{
    public Guid Id { get; set; }

    public Guid PortfolioId { get; set; }

    public Guid InstrumentId { get; set; }

    public required string Symbol { get; set; }

    public string Status { get; set; } = PositionStatus.Open;

    public decimal Quantity { get; set; }

    /// <summary>Average fill price, slippage included, fees excluded.</summary>
    public decimal AvgEntryPrice { get; set; }

    public decimal CostBasis { get; set; }

    public decimal? StopPrice { get; set; }

    public decimal? TargetPrice { get; set; }

    public Guid? EntryOrderId { get; set; }

    public Guid? ProposalId { get; set; }

    public Guid? StrategyVersionId { get; set; }

    public string? BrokerPositionId { get; set; }

    public DateTimeOffset OpenedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public decimal? ExitPrice { get; set; }

    public string? ExitReason { get; set; }

    /// <summary>Set on close, net of fees.</summary>
    public decimal? RealizedPnl { get; set; }

    public decimal FeesPaid { get; set; }

    /// <summary>Drives the NEW badge until the user opens the position.</summary>
    public DateTimeOffset? SeenAt { get; set; }

    /// <summary>The session of the last stop-proximity alert: at most one per position per session (§15.4).</summary>
    public DateOnly? StopAlertSession { get; set; }

    public long Version { get; set; }
}

public static class SnapshotKind
{
    public const string Eod = "eod";
    public const string Hourly = "hourly";
}

/// <summary>Row in <c>portfolio.snapshots</c> (§10.4): equity over time for the portfolio chart (T-090).</summary>
public sealed class PortfolioSnapshot
{
    public Guid PortfolioId { get; set; }

    public required string Kind { get; set; }

    public DateTimeOffset SnapshotAt { get; set; }

    public decimal Equity { get; set; }

    public decimal Cash { get; set; }

    public decimal Invested { get; set; }

    public decimal UnrealizedPnl { get; set; }

    public decimal RealizedPnlCum { get; set; }

    public decimal? BenchmarkClose { get; set; }

    public int PositionsCount { get; set; }

    public bool Stale { get; set; }
}
