using System.Globalization;
using System.Security.Cryptography;
using Astryx.Contracts.Audit;
using Astryx.Contracts.Events.Paper;
using Astryx.Contracts.MarketData;
using Astryx.Contracts.Paper;
using Astryx.Contracts.Portfolio;
using Astryx.Contracts.Trading;
using Astryx.Infrastructure.Security;
using Astryx.Modules.Paper.Domain;
using Astryx.Modules.Paper.Infrastructure.Persistence;
using Astryx.SharedKernel;
using Astryx.SharedKernel.Finance;
using Astryx.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Astryx.Modules.Paper.Application;

/// <summary>T6 body.</summary>
public sealed record PlaceOrderRequest(Guid ProposalId, Guid AssessmentId);

public sealed record FillDto(decimal Price, decimal ReferencePrice, decimal SlippagePerShare, decimal Slippage, decimal Fee, DateTimeOffset FilledAt);

public sealed record AttachedExitsDto(Guid OcoGroupId, decimal Stop, decimal Target);

/// <summary>OrderDto (SYSTEM_DESIGN.md §11.7). <c>fill</c> is set once filled; a working entry has none yet.</summary>
public sealed record OrderDto(
    Guid Id, string BrokerOrderId, string Mode, string Broker, string Status, string Purpose, string Symbol, string Side, string OrderType,
    decimal Quantity, decimal? StopPrice, decimal? LimitPrice, FillDto? Fill, AttachedExitsDto? Attached, Guid? PositionId, Guid? ProposalId,
    DateTimeOffset SubmittedAt, DateTimeOffset? ExpiresAt);

public sealed record OrderPageDto(IReadOnlyList<OrderDto> Items);

/// <summary>
/// T6–T8 (SYSTEM_DESIGN.md §11.7, §15.3). <see cref="PlaceAsync"/> must run inside an active
/// <see cref="Astryx.Infrastructure.Persistence.ModuleUnitOfWork"/>: portfolio lock, proposal lock and fresh risk run,
/// order, fill, position, OCO exits, ledger, proposal approval, outbox and audit commit together.
/// </summary>
public sealed class PaperOrderService(
    PaperDbContext db,
    ICurrentUser user,
    IPortfolioDirectory portfolios,
    IPortfolioLedger ledger,
    IPositionBook positions,
    IExecutionGate gate,
    IMarketSessionClock sessions,
    IConfiguration configuration)
{
    public static readonly Error NotFound = new("not_found", "No order with that id.");
    public static readonly Error NoPortfolio = new("conflict", "Your paper portfolio isn't ready yet.");
    public static readonly Error ConcurrencyConflict = new("concurrency_conflict", "The portfolio changed while placing the order. Review and try again.");

    private const string Base32 = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public async Task<Result<OrderDto>> PlaceAsync(PlaceOrderRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        var userId = user.RequireUserId();
        if (await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.ClientOrderId == idempotencyKey, cancellationToken) is { } placed)
        {
            return await ToDtoAsync(placed, cancellationToken); // the same intent, already executed
        }

        // §15.3 step 2: serialise money movement per portfolio before touching the proposal.
        if (await portfolios.GetSimulatedAsync(userId, cancellationToken) is not { } portfolioId || !await ledger.LockAsync(portfolioId, cancellationToken))
        {
            return NoPortfolio;
        }

        var prepared = await gate.PrepareAsync(userId, request.ProposalId, request.AssessmentId, cancellationToken);
        if (!prepared.IsSuccess)
        {
            return prepared.Error;
        }

        var ticket = prepared.Value;
        var now = sessions.Now;
        var session = await sessions.TodayAsync(cancellationToken);
        var costs = Costs();
        var slip = costs.Slip(ticket.Entry);
        var order = new Order
        {
            Id = Ids.New(),
            UserId = userId,
            PortfolioId = ticket.PortfolioId,
            InstrumentId = ticket.InstrumentId,
            Symbol = ticket.Symbol,
            ProposalId = ticket.ProposalId,
            AssessmentId = ticket.AssessmentId,
            ClientOrderId = idempotencyKey,
            BrokerOrderId = NewBrokerOrderId(),
            Purpose = OrderPurpose.Entry,
            Side = "buy",
            OrderType = ticket.OrderType,
            TimeInForce = "day",
            Quantity = ticket.Quantity,
            StopPrice = ticket.OrderType == "buy_stop" ? ticket.Entry : null,
            LimitPrice = ticket.OrderType == "limit" ? ticket.Entry : null,
            Status = OrderStatus.Accepted,
            SubmittedAt = now,
            ExpiresAt = session?.CloseAt,
        };
        db.Orders.Add(order);

        var simulated = PaperFillSimulator.EntryOnTick(ticket.OrderType, ticket.Entry, ticket.QuoteLast, slip);
        Fill? fill = null;
        Order[] exits = [];
        if (simulated is null)
        {
            // §15.3 step 7, not triggered: the order works and its worst-case cost is reserved.
            order.Status = OrderStatus.Working;
            order.ReservedAmount = RoundingPolicy.Ledger(PaperFillSimulator.Reservation(ticket.OrderType, ticket.Quantity, ticket.Entry, slip, costs.Fee));
            var reserved = await ledger.ReserveAsync(ticket.PortfolioId, order.ReservedAmount, cancellationToken);
            if (!reserved.IsSuccess)
            {
                return reserved.Error;
            }
        }
        else
        {
            fill = new Fill
            {
                Id = Ids.New(),
                OrderId = order.Id,
                PortfolioId = ticket.PortfolioId,
                InstrumentId = ticket.InstrumentId,
                Side = "buy",
                Quantity = ticket.Quantity,
                Price = simulated.Price,
                ReferencePrice = simulated.ReferencePrice,
                SlippagePerShare = simulated.SlippagePerShare,
                Fee = costs.Fee,
                QuoteAsOf = ticket.QuoteAsOf,
                FilledAt = now,
            };
            db.Fills.Add(fill);
            await db.SaveChangesAsync(cancellationToken); // the order row must exist before the ledger references it

            var entries = new List<LedgerEntry>
            {
                new("buy", -RoundingPolicy.Ledger(ticket.Quantity * simulated.Price), $"fill:{fill.Id}:principal",
                    $"Buy {Shares(ticket.Quantity)} {ticket.Symbol} @ {Price(simulated.Price)}", FillId: fill.Id, OrderId: order.Id),
            };
            if (costs.Fee > 0)
            {
                entries.Add(new("fee", -RoundingPolicy.Ledger(costs.Fee), $"fill:{fill.Id}:fee", "Commission", FillId: fill.Id, OrderId: order.Id));
            }

            var posted = await ledger.PostAsync(ticket.PortfolioId, entries, cancellationToken);
            if (!posted.IsSuccess)
            {
                return posted.Error.Code == "insufficient_funds" ? new Error("risk_check_failed", posted.Error.Message) : posted.Error;
            }

            var opened = await positions.OpenAsync(new OpenPositionRequest(
                ticket.PortfolioId, ticket.InstrumentId, ticket.Symbol, ticket.Quantity, simulated.Price, costs.Fee, ticket.Stop, ticket.Target,
                order.Id, ticket.ProposalId, ticket.StrategyVersionId, now), cancellationToken);
            if (!opened.IsSuccess)
            {
                return opened.Error;
            }

            order.Status = OrderStatus.Filled;
            order.FilledQuantity = ticket.Quantity;
            order.AvgFillPrice = simulated.Price;
            order.TriggeredAt = now;
            order.FilledAt = now;
            order.PositionId = opened.Value;
            exits = Exits(order, ticket, opened.Value, now);
            db.Orders.AddRange(exits);
            db.Publish(new OrderFilled(userId, order.Id, opened.Value, ticket.Symbol, order.Purpose, ticket.Quantity, simulated.Price, now));
            db.Publish(new PositionOpened(userId, opened.Value, ticket.PortfolioId, ticket.Symbol, ticket.Quantity, simulated.Price));
        }

        await gate.MarkApprovedAsync(ticket.ProposalId, cancellationToken);
        db.Publish(new OrderAccepted(userId, order.Id, ticket.ProposalId, ticket.Symbol, order.Status));
        db.Audit(new AuditEntry("paper.order_placed", AuditActor.User, userId, "order", order.Id.ToString(), Metadata: new
        {
            proposalId = ticket.ProposalId, assessmentId = ticket.AssessmentId, status = order.Status, quantity = ticket.Quantity,
            fillPrice = fill?.Price, reserved = order.ReservedAmount,
        }));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyConflict;
        }

        return ToDto(order, fill, exits);
    }

    public async Task<Result<OrderDto>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken) is { } order ? await ToDtoAsync(order, cancellationToken) : NotFound;

    public async Task<Result<OrderPageDto>> ListAsync(string? status, int? limit, CancellationToken cancellationToken)
    {
        var query = db.Orders.AsNoTracking();
        if (status is not null)
        {
            query = query.Where(o => o.Status == status);
        }

        var rows = await query.OrderByDescending(o => o.SubmittedAt).ThenByDescending(o => o.Id).Take(Math.Clamp(limit ?? 50, 1, 100)).ToListAsync(cancellationToken);
        var items = new List<OrderDto>(rows.Count);
        foreach (var row in rows)
        {
            items.Add(await ToDtoAsync(row, cancellationToken));
        }

        return new OrderPageDto(items);
    }

    /// <summary>OCO exits on the new position (§15.2): a GTC sell stop at the stop and a GTC limit sell at the target.</summary>
    private Order[] Exits(Order entry, ExecutionTicket ticket, Guid positionId, DateTimeOffset now)
    {
        var group = Ids.New();
        Order Exit(string purpose, string type, decimal? stop, decimal? limit) => new()
        {
            Id = Ids.New(), UserId = entry.UserId, PortfolioId = entry.PortfolioId, InstrumentId = entry.InstrumentId, Symbol = entry.Symbol,
            PositionId = positionId, ClientOrderId = $"exit:{positionId}:{purpose}", BrokerOrderId = NewBrokerOrderId(), Purpose = purpose, Side = "sell",
            OrderType = type, TimeInForce = "gtc", Quantity = ticket.Quantity, StopPrice = stop, LimitPrice = limit, OcoGroupId = group,
            Status = OrderStatus.Working, SubmittedAt = now,
        };
        return [Exit(OrderPurpose.ExitStop, "sell_stop", ticket.Stop, null), Exit(OrderPurpose.ExitTarget, "limit", null, ticket.Target)];
    }

    private async Task<OrderDto> ToDtoAsync(Order order, CancellationToken cancellationToken)
    {
        var fill = await db.Fills.AsNoTracking().FirstOrDefaultAsync(f => f.OrderId == order.Id, cancellationToken);
        var exits = order.Purpose == OrderPurpose.Entry && order.PositionId is { } positionId
            ? await db.Orders.AsNoTracking().Where(o => o.PositionId == positionId && o.OcoGroupId != null).ToArrayAsync(cancellationToken)
            : [];
        return ToDto(order, fill, exits);
    }

    private static OrderDto ToDto(Order o, Fill? f, Order[] exits)
    {
        var stop = exits.FirstOrDefault(e => e.Purpose == OrderPurpose.ExitStop);
        var target = exits.FirstOrDefault(e => e.Purpose == OrderPurpose.ExitTarget);
        return new OrderDto(
            o.Id, o.BrokerOrderId, "paper", o.Broker, o.Status, o.Purpose, o.Symbol, o.Side, o.OrderType, Whole(o.Quantity),
            Px(o.StopPrice), Px(o.LimitPrice),
            f is null ? null : new FillDto(Px(f.Price)!.Value, Px(f.ReferencePrice)!.Value, Px(f.SlippagePerShare)!.Value, RoundingPolicy.Ledger(f.SlippagePerShare * f.Quantity), RoundingPolicy.Ledger(f.Fee), f.FilledAt),
            stop is not null && target is not null ? new AttachedExitsDto(stop.OcoGroupId!.Value, Px(stop.StopPrice)!.Value, Px(target.LimitPrice)!.Value) : null,
            o.PositionId, o.ProposalId, o.SubmittedAt, o.ExpiresAt);
    }

    private static decimal? Px(decimal? price) => price is { } p ? Math.Round(p, 2, MidpointRounding.ToEven) : null;

    private static decimal Whole(decimal q) => Math.Round(q, 0, MidpointRounding.ToEven);

    private static string Shares(decimal q) => Whole(q).ToString(CultureInfo.InvariantCulture);

    private static string Price(decimal p) => p.ToString("F2", CultureInfo.InvariantCulture);

    private static string NewBrokerOrderId() => "SIM-" + string.Concat(RandomNumberGenerator.GetBytes(6).Select(b => Base32[b % 32]));

    private sealed record CostSettings(int Bps, decimal MinSlippage, decimal Fee)
    {
        public decimal Slip(decimal price) => Math.Max(MinSlippage, Math.Ceiling(price * Bps / 10_000m * 100m) / 100m);
    }

    private CostSettings Costs() => new(
        configuration.GetValue("Paper:SlippageBps", 1),
        decimal.Parse(configuration["Paper:MinSlippage"] ?? "0.01", CultureInfo.InvariantCulture),
        decimal.Parse(configuration["Paper:CommissionPerOrder"] ?? "0.00", CultureInfo.InvariantCulture));
}

/// <summary><see cref="IOpenOrders"/>: working entry orders per portfolio.</summary>
public sealed class OpenOrders(PaperDbContext db) : IOpenOrders
{
    public async Task<WorkingEntries> GetWorkingEntriesAsync(Guid portfolioId, CancellationToken cancellationToken)
    {
        var ids = await db.Orders.IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.PortfolioId == portfolioId && o.Purpose == OrderPurpose.Entry && (o.Status == OrderStatus.Working || o.Status == OrderStatus.Accepted))
            .Select(o => o.InstrumentId).ToListAsync(cancellationToken);
        return new WorkingEntries(ids.Count, ids);
    }
}
