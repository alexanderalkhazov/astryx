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

/// <summary>T10 response: what closing now would do, computed server-side.</summary>
public sealed record ClosePreviewDto(decimal Quantity, decimal MarketValue, decimal EstExitPrice, decimal EstFees, decimal EstRealizedPnl, DateTimeOffset AsOf);

public sealed record PositionDto(Guid Id, string Symbol, string Status, decimal Quantity, decimal AvgEntryPrice, decimal? ExitPrice, string? ExitReason, decimal? RealizedPnl, DateTimeOffset? ClosedAt);

/// <summary>T11 response.</summary>
public sealed record ClosePositionDto(OrderDto Order, PositionDto Position);

/// <summary>
/// T9–T11 (SYSTEM_DESIGN.md §11.7, §15.5). Cancel and close run inside a
/// <see cref="Astryx.Infrastructure.Persistence.ModuleUnitOfWork"/> under the portfolio lock, the same pattern as T6,
/// so a manual close racing a stop trigger produces exactly one exit (PAPER-06).
/// </summary>
public sealed class PositionActions(
    PaperDbContext db,
    ICurrentUser user,
    IPortfolioDirectory portfolios,
    IPortfolioLedger ledger,
    IPositionBook positionBook,
    IStockResearchSource research,
    IMarketSessionClock sessions,
    PaperExecution execution,
    PaperOrderService orders)
{
    public static readonly Error PositionNotFound = new("not_found", "No position with that id.");
    public static readonly Error NotOpen = new("position_not_open", "This position is no longer open.");
    public static readonly Error QuoteStale = new("quote_stale", "The quote is stale. Closing needs live data.");
    public static readonly Error MarketClosed = new("market_closed", "The regular session is closed.");
    public static readonly Error NotCancellable = new("conflict", "Only a working entry order can be cancelled.");

    public async Task<Result<ClosePreviewDto>> PreviewAsync(Guid positionId, CancellationToken cancellationToken)
    {
        var owned = await OwnedPositionAsync(positionId, cancellationToken);
        if (!owned.IsSuccess)
        {
            return owned.Error;
        }

        var position = owned.Value;
        if (position.Status != "open")
        {
            return NotOpen;
        }

        if (await research.GetQuoteAsync(position.Symbol, cancellationToken) is not { Stale: false } quote)
        {
            return QuoteStale;
        }

        var exitPrice = quote.Last - execution.Costs.Slip(quote.Last);
        var fees = execution.Costs.Fee;
        return new ClosePreviewDto(
            Math.Round(position.Quantity, 0), RoundingPolicy.Ledger(position.Quantity * quote.Last), Math.Round(exitPrice, 2), RoundingPolicy.Ledger(fees),
            RoundingPolicy.Ledger((position.Quantity * (exitPrice - position.AvgEntryPrice)) - position.FeesPaid - fees), quote.AsOf);
    }

    /// <summary>T11: a market exit at last − slippage; the OCO legs are cancelled. Full close only in the MVP.</summary>
    public async Task<Result<ClosePositionDto>> CloseAsync(Guid positionId, string idempotencyKey, CancellationToken cancellationToken)
    {
        var userId = user.RequireUserId();
        if (await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.ClientOrderId == idempotencyKey, cancellationToken) is { } done && done.PositionId is { } donePosition)
        {
            var again = await positionBook.LockAsync(donePosition, cancellationToken);
            return new ClosePositionDto(await OrderDtoAsync(done, cancellationToken), ToDto(again!));
        }

        if (await portfolios.GetSimulatedAsync(userId, cancellationToken) is not { } portfolioId || !await ledger.LockAsync(portfolioId, cancellationToken))
        {
            return PositionNotFound;
        }

        var owned = await OwnedPositionAsync(positionId, cancellationToken);
        if (!owned.IsSuccess)
        {
            return owned.Error;
        }

        var position = owned.Value;
        if (position.Status != "open")
        {
            return NotOpen;
        }

        var now = sessions.Now;
        if (await sessions.TodayAsync(cancellationToken) is not { } session || now < session.OpenAt || now >= session.CloseAt)
        {
            return MarketClosed;
        }

        if (await research.GetQuoteAsync(position.Symbol, cancellationToken) is not { Stale: false } quote)
        {
            return QuoteStale;
        }

        var slip = execution.Costs.Slip(quote.Last);
        var exit = new Order
        {
            Id = Ids.New(), UserId = userId, PortfolioId = position.PortfolioId, InstrumentId = position.InstrumentId, Symbol = position.Symbol, PositionId = position.Id,
            ClientOrderId = idempotencyKey, BrokerOrderId = PaperExecution.NewBrokerOrderId(), Purpose = OrderPurpose.ExitManual, Side = "sell", OrderType = "market",
            TimeInForce = "day", Quantity = position.Quantity, Status = OrderStatus.Accepted, SubmittedAt = now,
        };
        db.Orders.Add(exit);
        var siblings = await db.Orders.IgnoreQueryFilters().Where(o => o.PositionId == position.Id && o.OcoGroupId != null && o.Status == OrderStatus.Working).ToListAsync(cancellationToken);
        var closed = await execution.ExitAsync(exit, siblings, quote.Last - slip, quote.Last, slip, "manual", gapped: false, quote.AsOf, now, cancellationToken);
        if (!closed.IsSuccess)
        {
            return closed.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        var after = await positionBook.LockAsync(position.Id, cancellationToken);
        return new ClosePositionDto(await OrderDtoAsync(exit, cancellationToken), ToDto(after!));
    }

    /// <summary>T9: cancels a working entry and releases its reservation.</summary>
    public async Task<Result<OrderDto>> CancelAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var userId = user.RequireUserId();
        if (await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken) is not { } found)
        {
            return PaperOrderService.NotFound;
        }

        await ledger.LockAsync(found.PortfolioId, cancellationToken);
        var order = await db.Orders.SingleAsync(o => o.Id == orderId, cancellationToken);
        if (order.Purpose != OrderPurpose.Entry || order.Status != OrderStatus.Working || order.UserId != userId)
        {
            return NotCancellable;
        }

        if (order.ReservedAmount > 0)
        {
            var released = await ledger.ReserveAsync(order.PortfolioId, -order.ReservedAmount, cancellationToken);
            if (!released.IsSuccess)
            {
                return released.Error;
            }

            order.ReservedAmount = 0;
        }

        order.Status = OrderStatus.Cancelled;
        order.CancelledAt = sessions.Now;
        order.Version++;
        db.Publish(new Astryx.Contracts.Events.Paper.OrderAccepted(userId, order.Id, order.ProposalId, order.Symbol, order.Status));
        await db.SaveChangesAsync(cancellationToken);
        return await OrderDtoAsync(order, cancellationToken);
    }

    /// <summary>The user's position, row-locked when inside the close transaction; another user's is not found.</summary>
    private async Task<Result<PositionSnapshot>> OwnedPositionAsync(Guid positionId, CancellationToken cancellationToken)
    {
        var portfolioId = await portfolios.GetSimulatedAsync(user.RequireUserId(), cancellationToken);
        var position = await positionBook.LockAsync(positionId, cancellationToken);
        return position is null || position.PortfolioId != portfolioId ? PositionNotFound : position;
    }

    private async Task<OrderDto> OrderDtoAsync(Order order, CancellationToken cancellationToken) => (await orders.GetAsync(order.Id, cancellationToken)).Value;

    private static PositionDto ToDto(PositionSnapshot p) => new(
        p.Id, p.Symbol, p.Status, Math.Round(p.Quantity, 0), Math.Round(p.AvgEntryPrice, 2), p.ExitPrice is { } x ? Math.Round(x, 2) : null, p.ExitReason,
        p.RealizedPnl is { } r ? RoundingPolicy.Ledger(r) : null, p.ClosedAt);
}
