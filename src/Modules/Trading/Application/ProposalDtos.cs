namespace Astryx.Modules.Trading.Application;

/// <summary>T1 body: from an active scanner signal or a validated AI analysis.</summary>
public sealed record CreateProposalRequest(string Source, Guid? SignalId, Guid? AnalysisId);

public sealed record RejectProposalRequest(string? Reason);

public sealed record ProposalSourceDto(string Type, Guid? SignalId, Guid? AnalysisId, Guid? StrategyId, string? StrategyName, DateTimeOffset? SignalAt);

public sealed record ProposalAnalysisDto(string Headline, string Summary, IReadOnlyList<string> Evidence, IReadOnlyList<string> Invalidation, string? Score, string CopySource);

/// <summary>
/// T2 (SYSTEM_DESIGN.md §11.7). Always labelled as research; never an order. <c>assessment</c> arrives with the
/// risk engine (T-083) and is null until then.
/// </summary>
public sealed record ProposalDto(
    Guid Id, string Status, string Label, string Symbol, string Name, string Side, string Direction, string OrderType, string TimeInForce,
    decimal Entry, decimal Stop, decimal Target, decimal StopPct, decimal TargetPct, ProposalSourceDto Source, ProposalAnalysisDto Analysis,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? DecidedAt, string? RejectReason, object? Assessment);

public sealed record ProposalPageDto(IReadOnlyList<ProposalDto> Items, string? NextCursor);

/// <summary>T1 outcome: a new proposal (201) or the pending one that already exists for the instrument (200).</summary>
public sealed record CreateProposalOutcome(ProposalDto Proposal, bool Created);
