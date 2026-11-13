using Astryx.Contracts.Audit;
using Astryx.Contracts.Events.Paper;
using Astryx.Contracts.MarketData;
using Astryx.Contracts.Portfolio;
using Astryx.Infrastructure.Security;
using Astryx.Modules.Paper.Domain;
using Astryx.Modules.Paper.Infrastructure.Persistence;
using Astryx.SharedKernel;
using Astryx.SharedKernel.Finance;
using Astryx.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Paper.Application;

public sealed record ResetPaperAccountRequest(string? Confirm);

/// <summary>P11 response: what the reset did. The client reloads P1 for the new valuation.</summary>
public sealed record PaperResetDto(Guid PortfolioId, decimal CashBalance, decimal StartingCash, int ClosedPositions, int CancelledOrders, int ResetCount, DateTimeOffset EpochStartedAt);

/// <summary>
/// P11 paper account reset (SYSTEM_DESIGN.md §11.8, PAPER-08), in the caller's unit of work: cancel every working order
/// (releasing reservations), close every open position at its last price (<c>exit_reason=reset</c>, no fee or slippage,
/// booked as <c>reset_close</c>; the market need not be open), then let Portfolio post <c>reset_deposit</c> back to the
/// starting cash and start a new performance epoch. Replays are handled by the Idempotency-Key filter.
/// </summary>
public sealed class PaperResetService(
    PaperDbContext db,
    ICurrentUser user,
    IPortfolioDirectory portfolios,
    IPortfolioLedger ledger,
    IPortfolioStateSource states,
    IPositionBook positionBook,
    IPortfolioEpochs epochs,
    IStockResearchSource research,
    TimeProvider timeProvider)
{
    public const string Confirmation = "RESET";
    public static readonly Error ConfirmationRequired = new("validation_failed", "Type RESET to confirm the reset.");
    public static readonly Error NoPortfolio = new("not_found", "No paper portfolio for this account.");

    public async Task<Result<PaperResetDto>> ResetAsync(ResetPaperAccountRequest request, CancellationToken cancellationToken)
    {
        if (request.Confirm != Confirmation)
        {
            return ConfirmationRequired;
        }

        var userId = user.RequireUserId();
        if (await portfolios.GetSimulatedAsync(userId, cancellationToken) is not { } portfolioId || !await ledger.LockAsync(portfolioId, cancellationToken))
        {
            return NoPortfolio;
        }

        var now = timeProvider.GetUtcNow();
        var resetRef = $"reset:{portfolioId}:{Ids.New(timeProvider)}";

        // 1. Orders: working entries and the stop/target legs of open positions.
        var open = await db.Orders.Where(o => o.PortfolioId == portfolioId && (o.Status == OrderStatus.Working || o.Status == OrderStatus.Accepted)).ToListAsync(cancellationToken);
        foreach (var order in open)
        {
            order.Status = OrderStatus.Cancelled;
            order.CancelledAt = now;
            order.ReservedAmount = 0;
            order.Version++;
            db.Publish(new OrderAccepted(userId, order.Id, order.ProposalId, order.Symbol, order.Status));
        }

        await db.SaveChangesAsync(cancellationToken);
        var state = (await states.GetAsync(portfolioId, cancellationToken))!;
        if (state.ReservedCash > 0)
        {
            var released = await ledger.ReserveAsync(portfolioId, -state.ReservedCash, cancellationToken);
            if (!released.IsSuccess)
            {
                return released.Error;
            }
        }

        // 2. Positions at the last price (or flat at cost when there is no quote at all).
        var entries = new List<LedgerEntry>();
        foreach (var position in state.OpenPositions)
        {
            var snapshot = (await positionBook.LockAsync(position.PositionId, cancellationToken))!;
            var price = (await research.GetQuoteAsync(position.Symbol, cancellationToken))?.Last ?? snapshot.AvgEntryPrice;
            var closed = await positionBook.CloseAsync(position.PositionId, price, "reset", 0m, now, cancellationToken);
            if (!closed.IsSuccess)
            {
                return closed.Error;
            }

            var proceeds = RoundingPolicy.Ledger(position.Quantity * price);
            if (proceeds != 0)
            {
                entries.Add(new LedgerEntry("reset_close", proceeds, $"{resetRef}:close:{position.PositionId}", $"Paper account reset: closed {position.Symbol} at {price}", PositionId: position.PositionId));
            }
        }

        if (entries.Count > 0)
        {
            var posted = await ledger.PostAsync(portfolioId, entries, cancellationToken);
            if (!posted.IsSuccess)
            {
                return posted.Error;
            }
        }

        // 3. Cash back to the starting balance and a new epoch.
        var epoch = await epochs.ResetAsync(portfolioId, resetRef, now, cancellationToken);
        if (!epoch.IsSuccess)
        {
            return epoch.Error;
        }

        db.Publish(new PaperAccountReset(userId, portfolioId, state.OpenPositions.Count, open.Count, epoch.Value.StartingCash, now));
        db.Audit(new AuditEntry("paper.account_reset", AuditActor.User, userId, "portfolio", portfolioId.ToString(), Metadata: new
        {
            closedPositions = state.OpenPositions.Count, cancelledOrders = open.Count, deposit = epoch.Value.Deposit, resetCount = epoch.Value.ResetCount,
        }));
        await db.SaveChangesAsync(cancellationToken);
        return new PaperResetDto(portfolioId, epoch.Value.CashBalance, epoch.Value.StartingCash, state.OpenPositions.Count, open.Count, epoch.Value.ResetCount, epoch.Value.EpochStartedAt);
    }
}
