using System.Globalization;
using System.Security.Cryptography;
using Astryx.Contracts.Events.Paper;
using Astryx.Contracts.Portfolio;
using Astryx.Modules.Paper.Domain;
using Astryx.Modules.Paper.Infrastructure.Persistence;
using Astryx.SharedKernel;
using Astryx.SharedKernel.Finance;
using Astryx.SharedKernel.Results;
using Microsoft.Extensions.Configuration;

namespace Astryx.Modules.Paper.Application;

/// <summary>Paper cost settings (§15): slippage per share and a flat commission.</summary>
public sealed record PaperCosts(int Bps, decimal MinSlippage, decimal Fee)
{
    public decimal Slip(decimal price) => Math.Max(MinSlippage, Math.Ceiling(price * Bps / 10_000m * 100m) / 100m);

    public static PaperCosts From(IConfiguration configuration) => new(
        configuration.GetValue("Paper:SlippageBps", 1),
        decimal.Parse(configuration["Paper:MinSlippage"] ?? "0.01", CultureInfo.InvariantCulture),
        decimal.Parse(configuration["Paper:CommissionPerOrder"] ?? "0.00", CultureInfo.InvariantCulture));
}

/// <summary>
/// The fill paths shared by T6 and the position monitor (SYSTEM_DESIGN.md §15.2–15.4). Callers hold the portfolio
/// lock inside a <see cref="Astryx.Infrastructure.Persistence.ModuleUnitOfWork"/>; every method only stages changes
/// and ledger/position writes, and the caller saves and commits.
/// </summary>
public sealed class PaperExecution(PaperDbContext db, IPortfolioLedger ledger, IPositionBook positions, IConfiguration configuration)
{
    private const string Base32 = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public PaperCosts Costs { get; } = PaperCosts.From(configuration);

    public static string NewBrokerOrderId() => "SIM-" + string.Concat(RandomNumberGenerator.GetBytes(6).Select(b => Base32[b % 32]));

    /// <summary>
    /// Fills an entry: fill row, ledger buy (+ fee), position, OCO exits. Returns the fill and the exits. For an Alpaca
    /// order, <paramref name="brokerLegs"/> carries the bracket's stop-loss and take-profit order ids (§17.4), which the
    /// exit rows mirror; Alpaca is commission-free.
    /// </summary>
    public async Task<Result<(Fill Fill, Order[] Exits)>> FillEntryAsync(
        Order order, SimulatedFill simulated, decimal stop, decimal target, Guid? strategyVersionId, DateTimeOffset quoteAsOf, DateTimeOffset now, CancellationToken cancellationToken,
        BrokerLegs? brokerLegs = null)
    {
        var fee = order.Broker == BrokerCode.Mock ? Costs.Fee : 0m;
        var fill = new Fill
        {
            Id = Ids.New(),
            OrderId = order.Id,
            PortfolioId = order.PortfolioId,
            InstrumentId = order.InstrumentId,
            Side = "buy",
            Quantity = order.Quantity,
            Price = simulated.Price,
            ReferencePrice = simulated.ReferencePrice,
            SlippagePerShare = simulated.SlippagePerShare,
            Fee = fee,
            QuoteAsOf = quoteAsOf,
            FilledAt = now,
        };
        db.Fills.Add(fill);
        await db.SaveChangesAsync(cancellationToken); // the order and fill rows exist before the ledger points at them

        var entries = new List<LedgerEntry>
        {
            new("buy", -RoundingPolicy.Ledger(order.Quantity * simulated.Price), $"fill:{fill.Id}:principal",
                $"Buy {Shares(order.Quantity)} {order.Symbol} @ {Price(simulated.Price)}", FillId: fill.Id, OrderId: order.Id),
        };
        if (fee > 0)
        {
            entries.Add(new("fee", -RoundingPolicy.Ledger(fee), $"fill:{fill.Id}:fee", "Commission", FillId: fill.Id, OrderId: order.Id));
        }

        var posted = await ledger.PostAsync(order.PortfolioId, entries, cancellationToken);
        if (!posted.IsSuccess)
        {
            return posted.Error.Code == "insufficient_funds" ? new Error("risk_check_failed", posted.Error.Message) : posted.Error;
        }

        var opened = await positions.OpenAsync(new OpenPositionRequest(
            order.PortfolioId, order.InstrumentId, order.Symbol, order.Quantity, simulated.Price, fee, stop, target, order.Id, order.ProposalId!.Value, strategyVersionId, now), cancellationToken);
        if (!opened.IsSuccess)
        {
            return opened.Error;
        }

        order.Status = OrderStatus.Filled;
        order.FilledQuantity = order.Quantity;
        order.AvgFillPrice = simulated.Price;
        order.TriggeredAt = now;
        order.FilledAt = now;
        order.PositionId = opened.Value;
        order.Version++;
        var exits = Exits(order, stop, target, opened.Value, now, brokerLegs);
        db.Orders.AddRange(exits);
        db.Publish(new OrderFilled(order.UserId, order.Id, opened.Value, order.Symbol, order.Purpose, order.Quantity, simulated.Price, now));
        db.Publish(new PositionOpened(order.UserId, opened.Value, order.PortfolioId, order.Symbol, order.Quantity, simulated.Price));
        return (fill, exits);
    }

    /// <summary>Fills one OCO leg (or a manual exit), cancels the live siblings, closes the position and credits the sale.</summary>
    public async Task<Result<decimal>> ExitAsync(
        Order exit, IReadOnlyList<Order> siblings, decimal price, decimal reference, decimal slippagePerShare, string exitReason, bool gapped, DateTimeOffset quoteAsOf, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var fee = exit.Broker == BrokerCode.Mock ? Costs.Fee : 0m;
        var fill = new Fill
        {
            Id = Ids.New(),
            OrderId = exit.Id,
            PortfolioId = exit.PortfolioId,
            InstrumentId = exit.InstrumentId,
            Side = "sell",
            Quantity = exit.Quantity,
            Price = price,
            ReferencePrice = reference,
            SlippagePerShare = slippagePerShare,
            Fee = fee,
            QuoteAsOf = quoteAsOf,
            FilledAt = now,
        };
        exit.Status = OrderStatus.Filled;
        exit.FilledQuantity = exit.Quantity;
        exit.AvgFillPrice = price;
        exit.TriggeredAt = now;
        exit.FilledAt = now;
        exit.Version++;
        foreach (var sibling in siblings.Where(s => s.Id != exit.Id && s.Status is OrderStatus.Working or OrderStatus.Accepted))
        {
            sibling.Status = OrderStatus.Cancelled;
            sibling.CancelledAt = now;
            sibling.Version++;
        }

        db.Fills.Add(fill);
        await db.SaveChangesAsync(cancellationToken);

        var entries = new List<LedgerEntry>
        {
            new("sell", RoundingPolicy.Ledger(exit.Quantity * price), $"fill:{fill.Id}:principal",
                $"Sell {Shares(exit.Quantity)} {exit.Symbol} @ {Price(price)}", FillId: fill.Id, OrderId: exit.Id, PositionId: exit.PositionId),
        };
        if (fee > 0)
        {
            entries.Add(new("fee", -RoundingPolicy.Ledger(fee), $"fill:{fill.Id}:fee", "Commission", FillId: fill.Id, OrderId: exit.Id, PositionId: exit.PositionId));
        }

        var posted = await ledger.PostAsync(exit.PortfolioId, entries, cancellationToken);
        if (!posted.IsSuccess)
        {
            return posted.Error;
        }

        var closed = await positions.CloseAsync(exit.PositionId!.Value, price, exitReason, fee, now, cancellationToken);
        if (!closed.IsSuccess)
        {
            return closed.Error;
        }

        db.Publish(new OrderFilled(exit.UserId, exit.Id, exit.PositionId, exit.Symbol, exit.Purpose, exit.Quantity, price, now));
        db.Publish(new PositionClosed(exit.UserId, exit.PositionId.Value, exit.Symbol, exitReason, price, closed.Value, gapped));
        return closed.Value;
    }

    /// <summary>Expires a working day entry and releases what it reserved (§15.4 SessionCloseJob, PAPER-03).</summary>
    public async Task<Result<bool>> ExpireAsync(Order order, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (order.ReservedAmount > 0)
        {
            var released = await ledger.ReserveAsync(order.PortfolioId, -order.ReservedAmount, cancellationToken);
            if (!released.IsSuccess)
            {
                return released.Error;
            }
        }

        order.Status = OrderStatus.Expired;
        order.CancelledAt = now;
        order.Version++;
        db.Publish(new OrderExpired(order.UserId, order.Id, order.ProposalId, order.Symbol));
        return true;
    }

    private static Order[] Exits(Order entry, decimal stop, decimal target, Guid positionId, DateTimeOffset now, BrokerLegs? legs)
    {
        var group = Ids.New();
        Order Exit(string purpose, string type, decimal? stopPrice, decimal? limit, string? brokerOrderId) => new()
        {
            Id = Ids.New(),
            UserId = entry.UserId,
            PortfolioId = entry.PortfolioId,
            InstrumentId = entry.InstrumentId,
            Symbol = entry.Symbol,
            PositionId = positionId,
            ClientOrderId = $"exit:{positionId}:{purpose}",
            Broker = entry.Broker,
            Mode = entry.Mode,
            BrokerOrderId = brokerOrderId ?? NewBrokerOrderId(),
            Purpose = purpose,
            Side = "sell",
            OrderType = type,
            TimeInForce = "gtc",
            Quantity = entry.Quantity,
            StopPrice = stopPrice,
            LimitPrice = limit,
            OcoGroupId = group,
            Status = OrderStatus.Working,
            SubmittedAt = now,
        };
        return [Exit(OrderPurpose.ExitStop, "sell_stop", stop, null, legs?.StopLossOrderId), Exit(OrderPurpose.ExitTarget, "limit", null, target, legs?.TakeProfitOrderId)];
    }

    public static string Shares(decimal q) => Math.Round(q, 0, MidpointRounding.ToEven).ToString(CultureInfo.InvariantCulture);

    public static string Price(decimal p) => p.ToString("F2", CultureInfo.InvariantCulture);
}

/// <summary>The broker's order ids for a bracket's two exit legs (§17.4).</summary>
public sealed record BrokerLegs(string StopLossOrderId, string TakeProfitOrderId);
