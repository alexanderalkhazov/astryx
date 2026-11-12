using Astryx.SharedKernel.Results;

namespace Astryx.Contracts.Trading;

/// <summary>
/// What the paper engine may execute (SYSTEM_DESIGN.md §15.3 steps 3–5): a pending proposal re-assessed on a fresh
/// quote. <paramref name="AssessmentId"/> is the new, approvable assessment the order is placed against.
/// </summary>
public sealed record ExecutionTicket(
    Guid ProposalId, Guid AssessmentId, Guid PortfolioId, Guid InstrumentId, string Symbol, string OrderType, decimal Entry, decimal Stop, decimal Target,
    decimal Quantity, decimal QuoteLast, DateTimeOffset QuoteAsOf, Guid? StrategyVersionId);

/// <summary>
/// Owned by Trading (proposals and the risk engine), used by Paper inside the execution transaction. Paper is the
/// only caller of <see cref="MarkApprovedAsync"/>: a proposal is approved exactly when its order is accepted.
/// </summary>
public interface IExecutionGate
{
    /// <summary>
    /// Locks the proposal, checks it is the user's, pending and unexpired, that the assessment belongs to it and is
    /// valid, then re-runs the risk engine on a fresh quote (§14.5). Errors: <c>not_found</c>,
    /// <c>proposal_not_pending</c>, <c>assessment_stale</c>, <c>quote_stale</c>, <c>market_closed</c>, <c>risk_check_failed</c>.
    /// </summary>
    Task<Result<ExecutionTicket>> PrepareAsync(Guid userId, Guid proposalId, Guid assessmentId, CancellationToken cancellationToken);

    Task MarkApprovedAsync(Guid proposalId, CancellationToken cancellationToken);

    /// <summary>The proposal's levels and lineage, for attaching OCO exits when a working entry fills later.</summary>
    Task<ProposalLevels> LevelsAsync(Guid proposalId, CancellationToken cancellationToken);
}

public sealed record ProposalLevels(decimal Entry, decimal Stop, decimal Target, Guid? StrategyVersionId);
