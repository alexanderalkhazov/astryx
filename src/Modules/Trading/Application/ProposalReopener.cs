using Astryx.Contracts.Events;
using Astryx.Contracts.Events.Paper;
using Astryx.Modules.Trading.Domain;
using Astryx.Modules.Trading.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Trading.Application;

/// <summary>
/// The broker rejected, cancelled or expired an approved entry before it filled (SYSTEM_DESIGN.md §17.5, TASKS.md
/// T-144): the plan was never executed, so the proposal goes back to pending for the user to retry or reject. The
/// broker's reason stays on the order. An expired proposal, or one whose instrument already has a newer pending
/// proposal (one pending per instrument), expires instead. Idempotent: only an approved proposal changes.
/// </summary>
public sealed class ProposalReopener(TradingDbContext db, TimeProvider timeProvider) : IIntegrationEventHandler<OrderRejected>
{
    public async Task HandleAsync(OrderRejected e, IntegrationEventContext context, CancellationToken cancellationToken)
    {
        if (e.ProposalId is not { } proposalId
            || await db.Proposals.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(p => p.Id == proposalId && p.Status == ProposalStatus.Approved, cancellationToken) is not { } proposal)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var slotTaken = await db.Proposals.IgnoreQueryFilters().AnyAsync(
            p => p.UserId == proposal.UserId && p.InstrumentId == proposal.InstrumentId && p.Status == ProposalStatus.Pending, cancellationToken);
        if (proposal.ExpiresAt <= now || slotTaken)
        {
            await db.Proposals.IgnoreQueryFilters().Where(p => p.Id == proposalId && p.Status == ProposalStatus.Approved).ExecuteUpdateAsync(
                s => s.SetProperty(p => p.Status, ProposalStatus.Expired).SetProperty(p => p.RejectReason, "expired")
                    .SetProperty(p => p.DecidedAt, now).SetProperty(p => p.Version, p => p.Version + 1),
                cancellationToken);
            return;
        }

        await db.Proposals.IgnoreQueryFilters().Where(p => p.Id == proposalId && p.Status == ProposalStatus.Approved).ExecuteUpdateAsync(
            s => s.SetProperty(p => p.Status, ProposalStatus.Pending).SetProperty(p => p.DecidedAt, (DateTimeOffset?)null)
                .SetProperty(p => p.Version, p => p.Version + 1),
            cancellationToken);
    }
}
