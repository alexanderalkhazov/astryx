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
    PaperOrderService orders,
    BrokerCredentials brokerCredentials,
    Astryx.Modules.Paper.Infrastructure.Alpaca.IAlpacaTradingClient alpaca)
{
    public static readonly Error PositionNotFound = new("not_found", "No position with that id.");
    public static readonly Error NotOpen = new("position_not_open", "This position is no longer open.");
    public static readonly Error QuoteStale = new("quote_stale", "The quote is stale. Closing needs live data.");
    public static readonly Error MarketClosed = new("market_closed", "The regular session is closed.");
    public static readonly Error NotCancellable = new("conflict", "Only a working entry order can be cancelled.");
    public static readonly Error BrokerNotConnected = new("broker_unavailable", "Your Alpaca connection is unavailable. Reconnect it in Settings to close this position.");

    public async Task<Result<ClosePreviewDto>> PreviewAsync(Guid positionId, CancellationToken cancellationToken)
    {
        var owned = await OwnedPositionAsync(user.RequireUserId(), positionId, cancellationToken);
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
    public Task<Result<ClosePositionDto>> CloseAsync(Guid positionId, string idempotencyKey, CancellationToken cancellationToken) =>
        CloseForAsync(user.RequireUserId(), positionId, idempotencyKey, "manual", cancellationToken);

    /// <summary>
    /// Closes <paramref name="userId"/>'s position: the user's own close (<c>manual</c>) or the system's on a strategy exit
    /// (<c>strategy</c>, T-163). Must run inside a <see cref="Astryx.Infrastructure.Persistence.ModuleUnitOfWork"/>.
    /// </summary>
    public async Task<Result<ClosePositionDto>> CloseForAsync(Guid userId, Guid positionId, string idempotencyKey, string exitReason, CancellationToken cancellationToken)
    {
        if (await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.ClientOrderId == idempotencyKey, cancellationToken) is { } done && done.PositionId is { } donePosition)
        {
            var again = await positionBook.LockAsync(donePosition, cancellationToken);
            return new ClosePositionDto(await OrderDtoAsync(done, cancellationToken), ToDto(again!));
        }

        if (await portfolios.GetActiveAsync(userId, cancellationToken) is not { } active)
        {
            return PositionNotFound;
        }

        // §17.5: an Alpaca position is sold at Alpaca, before the portfolio lock, so no network call holds it.
        AlpacaClose? brokerClose = null;
        if (active.IsBroker)
        {
            var sent = await CloseAtAlpacaAsync(positionId, userId, cancellationToken);
            if (!sent.IsSuccess)
            {
                return sent.Error;
            }

            brokerClose = sent.Value;
        }

        if (!await ledger.LockAsync(active.Id, cancellationToken))
        {
            return PositionNotFound;
        }

        var owned = await OwnedPositionAsync(userId, positionId, cancellationToken);
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
        if (brokerClose is not null)
        {
            return await RecordAlpacaCloseAsync(position, brokerClose, userId, idempotencyKey, exitReason, now, cancellationToken);
        }

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
            Id = Ids.New(),
            UserId = userId,
            PortfolioId = position.PortfolioId,
            InstrumentId = position.InstrumentId,
            Symbol = position.Symbol,
            PositionId = position.Id,
            ClientOrderId = idempotencyKey,
            BrokerOrderId = PaperExecution.NewBrokerOrderId(),
            Purpose = OrderPurpose.ExitManual,
            Side = "sell",
            OrderType = "market",
            TimeInForce = "day",
            Quantity = position.Quantity,
            Status = OrderStatus.Accepted,
            SubmittedAt = now,
        };
        db.Orders.Add(exit);
        var siblings = await db.Orders.IgnoreQueryFilters().Where(o => o.PositionId == position.Id && o.OcoGroupId != null && o.Status == OrderStatus.Working).ToListAsync(cancellationToken);
        var closed = await execution.ExitAsync(exit, siblings, quote.Last - slip, quote.Last, slip, exitReason, gapped: false, quote.AsOf, now, cancellationToken);
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

    private sealed record AlpacaClose(Astryx.Modules.Paper.Infrastructure.Alpaca.AlpacaOrder Order);

    /// <summary>Cancels the bracket legs at Alpaca and market-sells the position there, waiting briefly for the fill.</summary>
    private async Task<Result<AlpacaClose>> CloseAtAlpacaAsync(Guid positionId, Guid userId, CancellationToken cancellationToken)
    {
        var now = sessions.Now;
        if (await sessions.TodayAsync(cancellationToken) is not { } session || now < session.OpenAt || now >= session.CloseAt)
        {
            return MarketClosed;
        }

        var legs = await db.Orders.AsNoTracking()
            .Where(o => o.PositionId == positionId && o.Broker == BrokerCode.Alpaca && o.OcoGroupId != null && o.Status == OrderStatus.Working)
            .ToListAsync(cancellationToken);
        var entry = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.PositionId == positionId && o.Purpose == OrderPurpose.Entry, cancellationToken);
        if (entry is null || entry.Broker != BrokerCode.Alpaca)
        {
            return PositionNotFound;
        }

        var connectionId = await db.BrokerConnections.AsNoTracking().Where(c => c.Status != BrokerConnectionStatus.Revoked).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(cancellationToken);
        if (connectionId is not { } id || await brokerCredentials.OpenAsync(id, cancellationToken) is not { } opened)
        {
            return BrokerNotConnected;
        }

        try
        {
            foreach (var leg in legs)
            {
                await alpaca.CancelOrderAsync(opened.Credentials, leg.BrokerOrderId, cancellationToken);
            }

            var closing = await alpaca.ClosePositionAsync(opened.Credentials, entry.Symbol, cancellationToken);
            for (var attempt = 0; attempt < 6 && !closing.IsFilled; attempt++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500L), cancellationToken);
                closing = await alpaca.GetOrderAsync(opened.Credentials, closing.Id, cancellationToken) ?? closing;
            }

            return new AlpacaClose(closing);
        }
        catch (Astryx.Infrastructure.Alpaca.AlpacaApiException ex)
        {
            return new Error("broker_unavailable", $"Alpaca couldn't close the position: {ex.Message}");
        }
    }

    /// <summary>Records Alpaca's close: the exit at Alpaca's price when it filled, else a submitted exit the sync settles.</summary>
    private async Task<Result<ClosePositionDto>> RecordAlpacaCloseAsync(PositionSnapshot position, AlpacaClose close, Guid userId, string idempotencyKey, string exitReason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var exit = new Order
        {
            Id = Ids.New(),
            UserId = userId,
            PortfolioId = position.PortfolioId,
            InstrumentId = position.InstrumentId,
            Symbol = position.Symbol,
            PositionId = position.Id,
            ClientOrderId = idempotencyKey,
            Broker = BrokerCode.Alpaca,
            Mode = "alpaca_paper",
            BrokerOrderId = close.Order.Id,
            Purpose = OrderPurpose.ExitManual,
            Side = "sell",
            OrderType = "market",
            TimeInForce = "day",
            Quantity = position.Quantity,
            Status = OrderStatus.Submitted,
            SubmittedAt = now,
        };
        db.Orders.Add(exit);
        var siblings = await db.Orders.IgnoreQueryFilters().Where(o => o.PositionId == position.Id && o.OcoGroupId != null && o.Status == OrderStatus.Working).ToListAsync(cancellationToken);
        if (close.Order is { IsFilled: true, FilledAvgPrice: { } price })
        {
            var closed = await execution.ExitAsync(exit, siblings, price, price, 0m, exitReason, gapped: false, close.Order.FilledAt ?? now, close.Order.FilledAt ?? now, cancellationToken);
            if (!closed.IsSuccess)
            {
                return closed.Error;
            }
        }
        else
        {
            foreach (var leg in siblings)
            {
                leg.Status = OrderStatus.Cancelled; // cancelled at Alpaca above
                leg.CancelledAt = now;
                leg.Version++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        var after = await positionBook.LockAsync(position.Id, cancellationToken);
        return new ClosePositionDto(await OrderDtoAsync(exit, cancellationToken), ToDto(after!));
    }

    /// <summary>The user's position, row-locked when inside the close transaction; another user's is not found.</summary>
    private async Task<Result<PositionSnapshot>> OwnedPositionAsync(Guid userId, Guid positionId, CancellationToken cancellationToken)
    {
        var portfolioId = (await portfolios.GetActiveAsync(userId, cancellationToken))?.Id;
        var position = await positionBook.LockAsync(positionId, cancellationToken);
        return position is null || position.PortfolioId != portfolioId ? PositionNotFound : position;
    }

    private async Task<OrderDto> OrderDtoAsync(Order order, CancellationToken cancellationToken) => (await orders.GetAsync(order.Id, cancellationToken)).Value;

    private static PositionDto ToDto(PositionSnapshot p) => new(
        p.Id, p.Symbol, p.Status, Math.Round(p.Quantity, 0), Math.Round(p.AvgEntryPrice, 2), p.ExitPrice is { } x ? Math.Round(x, 2) : null, p.ExitReason,
        p.RealizedPnl is { } r ? RoundingPolicy.Ledger(r) : null, p.ClosedAt);
}
