using Astryx.Contracts.Profiles;
using Astryx.Contracts.Trading;
using Astryx.Modules.Trading.Domain;
using Astryx.Modules.Trading.Infrastructure.Persistence;
using Astryx.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Trading.Application;

/// <summary>
/// <see cref="IExecutionGate"/> (SYSTEM_DESIGN.md §14.5, §15.3): the last word before an order. It never trusts the
/// client's assessment: the engine runs again on the fresh quote, and execution proceeds only if every check still
/// passes, the quantity is unchanged and the price moved at most 0.5%.
/// </summary>
public sealed class ExecutionGate(
    TradingDbContext db, AssessmentService assessments, IRiskSettingsSource riskSettings, Astryx.Contracts.MarketData.IMarketSessionClock sessions,
    Astryx.Contracts.Portfolio.IPortfolioStateSource portfolios) : IExecutionGate
{
    public const decimal MaxPriceDrift = 0.005m;

    public static readonly Error AssessmentNotFound = new("not_found", "No assessment with that id for this proposal.");
    public static readonly Error AssessmentStale = new("assessment_stale", "The numbers changed since this assessment. Review the new assessment before approving.");
    public static readonly Error RiskCheckFailed = new("risk_check_failed", "A pre-trade check now fails. Review the assessment.");
    public static readonly Error QuoteStale = new("quote_stale", "The quote is stale. Approvals need live data.");
    public static readonly Error MarketClosed = new("market_closed", "The regular session is closed or closes within 5 minutes.");
    public static readonly Error PortfolioHalted = new("portfolio_halted", "New entries are paused while this account's records are reviewed. Exits still work.");

    public async Task<Result<ExecutionTicket>> PrepareAsync(Guid userId, Guid proposalId, Guid assessmentId, CancellationToken cancellationToken)
    {
        var proposal = (await db.Proposals.FromSql($"SELECT * FROM trading.proposals WHERE id = {proposalId} FOR UPDATE")
            .IgnoreQueryFilters().AsNoTracking().ToListAsync(cancellationToken)).FirstOrDefault();
        if (proposal is null || proposal.UserId != userId)
        {
            return ProposalService.NotFound;
        }

        if (proposal.Status != ProposalStatus.Pending || proposal.ExpiresAt <= sessions.Now)
        {
            return ProposalService.NotPending;
        }

        // A reconciliation halt (§20.13) blocks every new entry, whatever the assessment says.
        if ((await portfolios.GetAsync(proposal.PortfolioId, cancellationToken))?.ReconciliationHaltedAt is not null)
        {
            return PortfolioHalted;
        }

        var previous = await db.RiskAssessments.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(a => a.Id == assessmentId && a.ProposalId == proposalId, cancellationToken);
        if (previous is null)
        {
            return AssessmentNotFound;
        }

        var limits = await riskSettings.GetAsync(userId, cancellationToken);
        if (!previous.Approvable || sessions.Now >= previous.ExpiresAt || previous.RiskSettingsVersion != limits.Version || previous.ProposalVersion != proposal.Version)
        {
            return AssessmentStale;
        }

        var rerun = await assessments.CreateAsync(proposal, previous.RequestedQuantity, previous.RiskPerTradePct, cancellationToken);
        if (!rerun.IsSuccess)
        {
            return rerun.Error;
        }

        var fresh = rerun.Value;
        if (!fresh.Approvable)
        {
            var failing = fresh.Checks.Where(c => c.Status == "fail").Select(c => c.Code).ToHashSet();
            return failing.Contains("session_open") ? MarketClosed : failing.Contains("data_fresh") ? QuoteStale : RiskCheckFailed;
        }

        var drift = previous.QuoteLast == 0 ? 1m : Math.Abs(fresh.Quote.Last - previous.QuoteLast) / previous.QuoteLast;
        if (fresh.Quantity != previous.Quantity || drift > MaxPriceDrift)
        {
            return AssessmentStale;
        }

        return new ExecutionTicket(
            proposal.Id, fresh.Id, proposal.PortfolioId, proposal.InstrumentId, proposal.Symbol, proposal.OrderType,
            proposal.EntryPrice, proposal.StopPrice, proposal.TargetPrice, fresh.Quantity, fresh.Quote.Last, fresh.Quote.AsOf, proposal.StrategyVersionId);
    }

    public async Task<ProposalLevels> LevelsAsync(Guid proposalId, CancellationToken cancellationToken) =>
        await db.Proposals.IgnoreQueryFilters().AsNoTracking().Where(p => p.Id == proposalId)
            .Select(p => new ProposalLevels(p.EntryPrice, p.StopPrice, p.TargetPrice, p.StrategyVersionId)).SingleAsync(cancellationToken);

    public async Task MarkApprovedAsync(Guid proposalId, CancellationToken cancellationToken)
    {
        var now = sessions.Now;
        await db.Proposals.IgnoreQueryFilters()
            .Where(p => p.Id == proposalId && p.Status == ProposalStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, ProposalStatus.Approved).SetProperty(p => p.DecidedAt, now).SetProperty(p => p.Version, p => p.Version + 1), cancellationToken);
    }
}

/// <summary><see cref="Astryx.Contracts.Trading.IPendingProposals"/>: the user's pending, unexpired proposals.</summary>
public sealed class PendingProposals(TradingDbContext db, Astryx.Contracts.MarketData.IMarketSessionClock sessions) : Astryx.Contracts.Trading.IPendingProposals
{
    public async Task<IReadOnlyList<Astryx.Contracts.Trading.PendingProposal>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = sessions.Now;
        return await db.Proposals.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.UserId == userId && p.Status == ProposalStatus.Pending && p.ExpiresAt > now)
            .OrderBy(p => p.ExpiresAt)
            .Select(p => new Astryx.Contracts.Trading.PendingProposal(p.Id, p.Symbol, p.ExpiresAt))
            .ToListAsync(cancellationToken);
    }
}

/// <summary><see cref="Astryx.Contracts.Trading.IProposalLineage"/>.</summary>
public sealed class ProposalLineageSource(TradingDbContext db) : Astryx.Contracts.Trading.IProposalLineage
{
    public async Task<Astryx.Contracts.Trading.ProposalLineage?> GetAsync(Guid proposalId, CancellationToken cancellationToken) =>
        await db.Proposals.IgnoreQueryFilters().AsNoTracking().Where(p => p.Id == proposalId)
            .Select(p => new Astryx.Contracts.Trading.ProposalLineage(p.Id, p.Source, p.StrategyId, p.StrategyName, p.SourceAt)).SingleOrDefaultAsync(cancellationToken);
}
