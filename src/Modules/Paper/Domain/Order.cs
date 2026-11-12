using Astryx.Infrastructure.Security;

namespace Astryx.Modules.Paper.Domain;

public static class OrderStatus
{
    public const string Accepted = "accepted";
    public const string Working = "working";
    public const string Filled = "filled";
    public const string Cancelled = "cancelled";
    public const string Expired = "expired";
    public const string Rejected = "rejected";
}

public static class OrderPurpose
{
    public const string Entry = "entry";
    public const string ExitStop = "exit_stop";
    public const string ExitTarget = "exit_target";
    public const string ExitManual = "exit_manual";
    public const string ExitReset = "exit_reset";
}

/// <summary>Row in <c>trading.orders</c> (SYSTEM_DESIGN.md §10.4), written only by the paper engine.</summary>
public sealed class Order : IUserOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid PortfolioId { get; set; }

    public Guid InstrumentId { get; set; }

    public required string Symbol { get; set; }

    public Guid? ProposalId { get; set; }

    public Guid? AssessmentId { get; set; }

    public Guid? PositionId { get; set; }

    /// <summary>The Idempotency-Key for entries; <c>exit:{positionId}:{purpose}</c> for exits.</summary>
    public required string ClientOrderId { get; set; }

    public string Broker { get; set; } = "mock";

    /// <summary><c>SIM-</c> plus six base32 characters.</summary>
    public required string BrokerOrderId { get; set; }

    public string Mode { get; set; } = "simulated";

    public required string Purpose { get; set; }

    public required string Side { get; set; }

    public required string OrderType { get; set; }

    public required string TimeInForce { get; set; }

    public decimal Quantity { get; set; }

    public decimal? StopPrice { get; set; }

    public decimal? LimitPrice { get; set; }

    public Guid? OcoGroupId { get; set; }

    public required string Status { get; set; }

    public decimal FilledQuantity { get; set; }

    public decimal? AvgFillPrice { get; set; }

    public string? RejectCode { get; set; }

    public DateTimeOffset SubmittedAt { get; set; }

    public DateTimeOffset? TriggeredAt { get; set; }

    public DateTimeOffset? FilledAt { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Cash held back for a working entry (released when it fills, expires or is cancelled).</summary>
    public decimal ReservedAmount { get; set; }

    public int Version { get; set; } = 1;
}

/// <summary>Row in <c>trading.fills</c>: one simulated execution (no partial fills in the MVP).</summary>
public sealed class Fill
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public int Sequence { get; set; } = 1;

    public Guid PortfolioId { get; set; }

    public Guid InstrumentId { get; set; }

    public required string Side { get; set; }

    public decimal Quantity { get; set; }

    public decimal Price { get; set; }

    public decimal ReferencePrice { get; set; }

    /// <summary>Adverse slippage per share, ≥ 0.</summary>
    public decimal SlippagePerShare { get; set; }

    public decimal Fee { get; set; }

    public DateTimeOffset QuoteAsOf { get; set; }

    public DateTimeOffset FilledAt { get; set; }

    public string SimulationModel { get; set; } = PaperFillSimulator.Model;
}
