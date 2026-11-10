namespace Astryx.Modules.Portfolio.Domain;

public static class PortfolioMode
{
    public const string Simulated = "simulated";
    public const string EToroDemo = "etoro_demo";
    public const string EToroReal = "etoro_real";
}

/// <summary>Row in <c>portfolio.portfolios</c> (SYSTEM_DESIGN.md §10.4). Cash is the sum of its ledger, cached here.</summary>
public sealed class Portfolio
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
