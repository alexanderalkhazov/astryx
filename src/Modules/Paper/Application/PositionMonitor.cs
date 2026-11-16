using Astryx.Contracts.Events;
using Astryx.Contracts.Events.MarketData;
using Astryx.Contracts.Events.Paper;
using Astryx.Contracts.MarketData;
using Astryx.Contracts.Portfolio;
using Astryx.Contracts.Trading;
using Astryx.Infrastructure.Jobs;
using Astryx.Infrastructure.Observability;
using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Paper.Domain;
using Astryx.Modules.Paper.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Astryx.Modules.Paper.Application;

/// <summary>
/// <c>PositionMonitor</c> (SYSTEM_DESIGN.md §15.4): on each quote tick, working entries and OCO exits for the
/// instrument are evaluated one portfolio at a time, each in its own transaction under the portfolio lock, with every
/// order re-read and required to still be working. Only one path can fill an exit, so a concurrent manual close or a
/// re-delivered event changes nothing (PAPER-06). Also raises the once-per-session stop-proximity alert.
/// </summary>
public sealed class PositionMonitor(IServiceProvider services, ILogger<PositionMonitor> logger) : IIntegrationEventHandler<QuotesUpdated>
{
    public async Task HandleAsync(QuotesUpdated integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken)
    {
        foreach (var quote in integrationEvent.Quotes)
        {
            await EvaluateAsync(quote, cancellationToken);
        }
    }

    public async Task EvaluateAsync(QuoteChange quote, CancellationToken cancellationToken)
    {
        List<Guid> portfolios;
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PaperDbContext>();
            var withOrders = await db.Orders.IgnoreQueryFilters()
                .Where(o => o.InstrumentId == quote.InstrumentId && o.Status == OrderStatus.Working).Select(o => o.PortfolioId).Distinct().ToListAsync(cancellationToken);
            portfolios = withOrders;
        }

        foreach (var portfolioId in portfolios)
        {
            try
            {
                await EvaluatePortfolioAsync(portfolioId, quote, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One portfolio's failure must not stop the others; the next tick evaluates it again.
                logger.LogError(ex, "Position monitor failed for portfolio {PortfolioId} on {Symbol}", portfolioId, quote.Symbol);
            }
        }
    }

    private async Task EvaluatePortfolioAsync(Guid portfolioId, QuoteChange quote, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var unit = sp.GetRequiredService<ModuleUnitOfWork>();
        await unit.BeginAsync(cancellationToken);
        if (!await sp.GetRequiredService<IPortfolioLedger>().LockAsync(portfolioId, cancellationToken))
        {
            return;
        }

        var db = sp.GetRequiredService<PaperDbContext>();
        var execution = sp.GetRequiredService<PaperExecution>();
        var sessions = sp.GetRequiredService<IMarketSessionClock>();
        var now = sessions.Now;
        AstryxTelemetry.MonitorLag(now - quote.AsOf);
        var window = PriceWindow.Tick(quote.Last);
        var working = await db.Orders.IgnoreQueryFilters()
            .Where(o => o.PortfolioId == portfolioId && o.InstrumentId == quote.InstrumentId && o.Status == OrderStatus.Working)
            .ToListAsync(cancellationToken);

        // A reconciliation halt (§20.13) keeps working entries from filling; exits below still run.
        var halted = (await sp.GetRequiredService<IPortfolioStateSource>().GetAsync(portfolioId, cancellationToken))?.ReconciliationHaltedAt is not null;
        foreach (var entry in working.Where(o => o.Purpose == OrderPurpose.Entry && !halted))
        {
            var level = entry.StopPrice ?? entry.LimitPrice ?? 0m;
            if (TriggerEvaluator.Entry(entry.OrderType, level, window, execution.Costs.Slip(level)) is not { } simulated)
            {
                continue;
            }

            var levels = await sp.GetRequiredService<IExecutionGate>().LevelsAsync(entry.ProposalId!.Value, cancellationToken);
            if (entry.ReservedAmount > 0)
            {
                var released = await sp.GetRequiredService<IPortfolioLedger>().ReserveAsync(portfolioId, -entry.ReservedAmount, cancellationToken);
                if (!released.IsSuccess)
                {
                    logger.LogError("Could not release the reservation of order {OrderId}: {Error}", entry.Id, released.Error.Message);
                    return;
                }

                entry.ReservedAmount = 0;
            }

            var filled = await execution.FillEntryAsync(entry, simulated, levels.Stop, levels.Target, levels.StrategyVersionId, quote.AsOf, now, cancellationToken);
            if (!filled.IsSuccess)
            {
                logger.LogWarning("Entry {OrderId} triggered but could not fill: {Error}", entry.Id, filled.Error.Message);
                return; // roll back: the order stays working with its reservation
            }
        }

        foreach (var group in working.Where(o => o.PositionId is not null && o.Purpose is OrderPurpose.ExitStop or OrderPurpose.ExitTarget).GroupBy(o => o.PositionId!.Value))
        {
            var stopLeg = group.FirstOrDefault(o => o.Purpose == OrderPurpose.ExitStop);
            var targetLeg = group.FirstOrDefault(o => o.Purpose == OrderPurpose.ExitTarget);
            if (stopLeg?.StopPrice is not { } stop || targetLeg?.LimitPrice is not { } target)
            {
                continue;
            }

            var position = await sp.GetRequiredService<IPositionBook>().LockAsync(group.Key, cancellationToken);
            if (position is not { Status: "open" })
            {
                continue; // closed by another path while we waited for the lock
            }

            if (TriggerEvaluator.Exit(stop, target, window, execution.Costs.Slip(stop)) is { } exit)
            {
                var leg = exit.Purpose == OrderPurpose.ExitStop ? stopLeg : targetLeg;
                var closed = await execution.ExitAsync(leg, [.. group], exit.Price, exit.ReferencePrice, exit.SlippagePerShare,
                    exit.Purpose == OrderPurpose.ExitStop ? "stop" : "target", exit.GappedThroughStop, quote.AsOf, now, cancellationToken);
                if (!closed.IsSuccess)
                {
                    logger.LogWarning("Exit for position {PositionId} triggered but could not fill: {Error}", group.Key, closed.Error.Message);
                    return;
                }
            }
            else if (TriggerEvaluator.NearStop(quote.Last, stop) && await sessions.TodayAsync(cancellationToken) is { } session
                && await sp.GetRequiredService<IPositionBook>().MarkStopAlertAsync(group.Key, session.Date, cancellationToken))
            {
                db.Publish(new StopProximityReached(stopLeg.UserId, group.Key, stopLeg.Symbol, quote.Last, stop));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await unit.CommitAsync(cancellationToken);
    }
}

/// <summary><c>SessionCloseJob</c> for orders (§15.4, PAPER-03): every minute, working day entries past their expiry expire.</summary>
public sealed record DayOrderExpiryJob : IJob
{
    public static string JobType => "paper.day_order_expiry.v1";
}

public sealed class DayOrderExpiryHandler(IServiceProvider services, TimeProvider timeProvider, ILogger<DayOrderExpiryHandler> logger) : IJobHandler<DayOrderExpiryJob>
{
    public async Task HandleAsync(DayOrderExpiryJob job, JobContext context, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        List<(Guid Order, Guid Portfolio)> due;
        await using (var scope = services.CreateAsyncScope())
        {
            due = (await scope.ServiceProvider.GetRequiredService<PaperDbContext>().Orders.IgnoreQueryFilters()
                .Where(o => o.Status == OrderStatus.Working && o.TimeInForce == "day" && o.ExpiresAt <= now)
                .Select(o => new { o.Id, o.PortfolioId }).ToListAsync(cancellationToken)).Select(o => (o.Id, o.PortfolioId)).ToList();
        }

        foreach (var (orderId, portfolioId) in due)
        {
            await using var scope = services.CreateAsyncScope();
            var unit = scope.ServiceProvider.GetRequiredService<ModuleUnitOfWork>();
            await unit.BeginAsync(cancellationToken);
            await scope.ServiceProvider.GetRequiredService<IPortfolioLedger>().LockAsync(portfolioId, cancellationToken);
            var db = scope.ServiceProvider.GetRequiredService<PaperDbContext>();
            var order = await db.Orders.IgnoreQueryFilters().SingleAsync(o => o.Id == orderId, cancellationToken);
            if (order.Status != OrderStatus.Working)
            {
                continue; // filled or cancelled meanwhile
            }

            var expired = await scope.ServiceProvider.GetRequiredService<PaperExecution>().ExpireAsync(order, now, cancellationToken);
            if (!expired.IsSuccess)
            {
                logger.LogError("Could not expire order {OrderId}: {Error}", orderId, expired.Error.Message);
                continue;
            }

            await db.SaveChangesAsync(cancellationToken);
            await unit.CommitAsync(cancellationToken);
        }
    }
}
