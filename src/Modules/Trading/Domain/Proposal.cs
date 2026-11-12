using Astryx.Infrastructure.Security;

namespace Astryx.Modules.Trading.Domain;

public static class ProposalStatus
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Expired = "expired";
}

public static class ProposalSource
{
    public const string Signal = "signal";
    public const string Analysis = "analysis";
}

/// <summary>
/// Row in <c>trading.proposals</c> (SYSTEM_DESIGN.md §7.9, §10.4): a research plan, never an order. Levels are
/// server-owned; only the Paper module sets <see cref="ProposalStatus.Approved"/>, when an order is accepted.
/// </summary>
public sealed class Proposal : IUserOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid PortfolioId { get; set; }

    public Guid InstrumentId { get; set; }

    public required string Symbol { get; set; }

    public required string Source { get; set; }

    public Guid? SignalId { get; set; }

    public Guid? AnalysisResultId { get; set; }

    public Guid? StrategyId { get; set; }

    public Guid? StrategyVersionId { get; set; }

    public string? StrategyName { get; set; }

    public DateTimeOffset? SourceAt { get; set; }

    public string Side { get; set; } = "buy";

    public required string OrderType { get; set; }

    public string TimeInForce { get; set; } = "day";

    public decimal EntryPrice { get; set; }

    public decimal StopPrice { get; set; }

    public decimal TargetPrice { get; set; }

    public required string Headline { get; set; }

    public required string Summary { get; set; }

    /// <summary><c>template</c> (deterministic copy) or <c>ai</c>.</summary>
    public required string CopySource { get; set; }

    /// <summary>JSON array of strings.</summary>
    public required string Evidence { get; set; }

    /// <summary>JSON array of strings.</summary>
    public required string Invalidation { get; set; }

    public decimal? Score { get; set; }

    public string Status { get; set; } = ProposalStatus.Pending;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary><c>user_rejected</c>, <c>expired</c> or <c>superseded</c>.</summary>
    public string? RejectReason { get; set; }

    /// <summary>The user's free-text reason (≤ 200) when they rejected it.</summary>
    public string? RejectNote { get; set; }

    /// <summary>Incremented on any change; part of the risk assessment inputs hash (§14.5).</summary>
    public int Version { get; set; } = 1;
}
