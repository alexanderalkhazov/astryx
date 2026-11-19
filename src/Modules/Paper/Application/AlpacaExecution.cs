using System.Collections.Concurrent;
using Astryx.Contracts.Audit;
using Astryx.Contracts.Events.Paper;
using Astryx.Contracts.Portfolio;
using Astryx.Contracts.Trading;
using Astryx.Infrastructure.Alpaca;
using Astryx.Infrastructure.Jobs;
using Astryx.Infrastructure.Observability;
using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Paper.Domain;
using Astryx.Modules.Paper.Infrastructure.Alpaca;
using Astryx.Modules.Paper.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Astryx.Modules.Paper.Application;

/// <summary>Sends one committed Alpaca entry to Alpaca (SYSTEM_DESIGN.md §17.5, TASKS.md T-144).</summary>
public sealed record SubmitBrokerOrderJob(Guid OrderId) : IJob
{
    public static string JobType => "paper.broker_submit.v1";
}

/// <summary>Asks Alpaca how one entry is doing and mirrors the answer; re-enqueues itself with backoff while it works.</summary>
public sealed record SettleBrokerOrderJob(Guid OrderId, int Attempt) : IJob
{
    public static string JobType => "paper.broker_settle.v1";
}

/// <summary>Every minute: settles open Alpaca entries and mirrors filled bracket legs as exits (§17.7, T-145).</summary>
public sealed record BrokerSyncJob : IJob
{
    public static string JobType => "paper.broker_sync.v1";
}

/// <summary>
/// The Alpaca side of order execution (SYSTEM_DESIGN.md §17.5, §17.7). Alpaca is authoritative for fills (ADR-021): this
/// class never decides a price, it records what Alpaca reports through the same <see cref="PaperExecution"/> paths the
/// simulator uses, so ledger, positions, events and the UI behave the same.
/// <list type="bullet">
/// <item>No network call runs inside a database transaction: Alpaca is called first, then a unit of work records the result
/// under the portfolio lock, re-reading the order so a concurrent settle cannot record it twice.</item>
/// <item>A lost submit is never blindly resent: the order is looked up by its <c>client_order_id</c> (the order id) first.</item>
/// </list>
/// </summary>
public sealed class AlpacaExecution(IServiceProvider services, IAlpacaTradingClient alpaca, TimeProvider timeProvider, ILogger<AlpacaExecution> logger)
{
    private const int MaxSettleBackoffAttempts = 8;

    /// <summary>Submits the bracket. Throws on a transient failure so the job runner retries it (with the same client id).</summary>
    /// <summary>
    /// Sends an entry once. Overlapping attempts for the same order in this process (a job retried while the first
    /// attempt is still waiting on Alpaca) return at once; across processes Alpaca's 422 on a duplicate
    /// <c>client_order_id</c> and the lookup below keep it to one order (§17.5).
    /// </summary>
    public async Task SubmitAsync(Guid orderId, CancellationToken cancellationToken)
    {
        if (!_submitting.TryAdd(orderId, 0))
        {
            return;
        }

        try
        {
            await SubmitOnceAsync(orderId, cancellationToken);
        }
        finally
        {
            _submitting.TryRemove(orderId, out _);
        }
    }

    private readonly ConcurrentDictionary<Guid, byte> _submitting = new();

    private async Task SubmitOnceAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var (order, credentials) = await LoadAsync(orderId, cancellationToken);
        if (order is null || order.Status != OrderStatus.Submitting)
        {
            return; // already submitted, settled or rejected
        }

        if (credentials is null)
        {
            await CloseUnfilledAsync(orderId, OrderStatus.Rejected, "broker_not_connected", "The Alpaca connection is gone.", cancellationToken);
            return;
        }

        AlpacaOrder? placed;
        try
        {
            placed = await alpaca.GetOrderByClientIdAsync(credentials, ClientId(order), cancellationToken); // a retry after a lost response
            if (placed is null)
            {
                ProposalLevels levels;
                await using (var scope = services.CreateAsyncScope())
                {
                    levels = await scope.ServiceProvider.GetRequiredService<IExecutionGate>().LevelsAsync(order.ProposalId!.Value, cancellationToken);
                }

                placed = await alpaca.SubmitBracketAsync(credentials, new AlpacaBracketRequest(
                    order.Symbol, order.Quantity, AlpacaType(order.OrderType), order.StopPrice, order.LimitPrice, levels.Stop, levels.Target, ClientId(order)), cancellationToken);
            }
        }
        catch (AlpacaApiException ex) when (ex.Failure is AlpacaFailure.Invalid or AlpacaFailure.InsufficientFunds or AlpacaFailure.Forbidden or AlpacaFailure.CredentialsInvalid)
        {
            // Alpaca answers a duplicate client_order_id with 422 (verified in Paper, Q-22): if it already holds this
            // order, a retry raced the lookup above, so continue with Alpaca's order rather than rejecting ours.
            placed = ex.Failure == AlpacaFailure.Invalid ? await alpaca.GetOrderByClientIdAsync(credentials, ClientId(order), cancellationToken) : null;
            if (placed is null)
            {
                logger.LogWarning("Alpaca refused order {OrderId}: {Reason}", orderId, ex.Message);
                await CloseUnfilledAsync(orderId, OrderStatus.Rejected, RejectCode(ex.Failure), ex.Message, cancellationToken);
                return;
            }
        }

        await using (var scope = services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var unit = sp.GetRequiredService<ModuleUnitOfWork>();
            await unit.BeginAsync(cancellationToken);
            var db = sp.GetRequiredService<PaperDbContext>();
            var row = await db.Orders.IgnoreQueryFilters().SingleAsync(o => o.Id == orderId, cancellationToken);
            if (row.Status == OrderStatus.Submitting)
            {
                row.BrokerOrderId = placed.Id;
                row.Status = OrderStatus.Submitted;
                row.Version++;
                db.Publish(new OrderAccepted(row.UserId, row.Id, row.ProposalId, row.Symbol, row.Status));
                await sp.GetRequiredService<IJobQueue>().EnqueueAsync(new SettleBrokerOrderJob(orderId, 0), new EnqueueOptions { RunAfter = timeProvider.GetUtcNow() + TimeSpan.FromSeconds(1L) }, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await unit.CommitAsync(cancellationToken);
            }
        }
    }

    /// <summary>Mirrors the entry's state at Alpaca. Returns true when it reached a final state.</summary>
    public async Task<bool> SettleAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var (order, credentials) = await LoadAsync(orderId, cancellationToken);
        if (order is null || order.Status is not (OrderStatus.Submitted or OrderStatus.Working or OrderStatus.Submitting))
        {
            return true;
        }

        if (credentials is null)
        {
            return false; // reconnecting resumes it; reconciliation flags it if it never comes back
        }

        var remote = order.Status == OrderStatus.Submitting
            ? await alpaca.GetOrderByClientIdAsync(credentials, ClientId(order), cancellationToken)
            : await alpaca.GetOrderAsync(credentials, order.BrokerOrderId, cancellationToken);
        if (remote is null)
        {
            return false;
        }

        if (remote.IsFilled || (remote.FilledQuantity > 0 && remote.Status is "canceled" or "expired" or "done_for_day"))
        {
            await RecordEntryFillAsync(orderId, remote, cancellationToken);
            return true;
        }

        if (remote.IsClosedUnfilled)
        {
            var status = remote.Status switch { "expired" or "done_for_day" => OrderStatus.Expired, "rejected" => OrderStatus.Rejected, _ => OrderStatus.Cancelled };
            await CloseUnfilledAsync(orderId, status, $"alpaca_{remote.Status}", $"Alpaca reported the order {remote.Status}.", cancellationToken);
            return true;
        }

        if (order.Status != OrderStatus.Working && remote.Status is "new" or "accepted" or "pending_new" or "partially_filled" or "accepted_for_bidding")
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PaperDbContext>();
            var row = await db.Orders.IgnoreQueryFilters().SingleAsync(o => o.Id == orderId, cancellationToken);
            if (row.Status == OrderStatus.Submitted)
            {
                row.Status = OrderStatus.Working; // resting at Alpaca: a buy stop waiting for its trigger, or a limit
                row.Version++;
                db.Publish(new OrderAccepted(row.UserId, row.Id, row.ProposalId, row.Symbol, row.Status));
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        return false;
    }

    public async Task SettleWithBackoffAsync(Guid orderId, int attempt, CancellationToken cancellationToken)
    {
        if (await SettleAsync(orderId, cancellationToken) || attempt >= MaxSettleBackoffAttempts)
        {
            return; // after the backoff window the one-minute sync keeps watching
        }

        await using var scope = services.CreateAsyncScope();
        var delay = TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempt + 1, 5)));
        await scope.ServiceProvider.GetRequiredService<IJobQueue>().EnqueueAsync(new SettleBrokerOrderJob(orderId, attempt + 1), new EnqueueOptions { RunAfter = timeProvider.GetUtcNow() + delay }, cancellationToken);
    }

    /// <summary>One sync pass for every connected account (§17.7).</summary>
    public async Task SyncAsync(CancellationToken cancellationToken)
    {
        List<(Guid Id, string Purpose)> open;
        await using (var scope = services.CreateAsyncScope())
        {
            open = (await scope.ServiceProvider.GetRequiredService<PaperDbContext>().Orders.IgnoreQueryFilters().AsNoTracking()
                .Where(o => o.Broker == BrokerCode.Alpaca && (o.Status == OrderStatus.Submitted || o.Status == OrderStatus.Working || o.Status == OrderStatus.Submitting))
                .Select(o => new { o.Id, o.Purpose }).ToListAsync(cancellationToken)).Select(o => (o.Id, o.Purpose)).ToList();
        }

        foreach (var (id, purpose) in open)
        {
            try
            {
                if (purpose == OrderPurpose.Entry)
                {
                    await SettleAsync(id, cancellationToken);
                }
                else
                {
                    await SettleExitAsync(id, cancellationToken);
                }
            }
            catch (AlpacaApiException ex)
            {
                logger.LogWarning("Alpaca sync for order {OrderId} failed: {Reason}", id, ex.Message);
            }
        }

        await ReportStuckAsync(cancellationToken);
        await ReconcileAccountsAsync(cancellationToken);
    }

    /// <summary>An entry Alpaca has neither filled nor closed this long after submission alerts (T-144).</summary>
    public static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(10L);

    /// <summary>Counts entries still sending or unacknowledged after <see cref="StuckAfter"/> for the <c>BrokerOrderStuck</c> alert.</summary>
    public async Task<int> ReportStuckAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var before = timeProvider.GetUtcNow() - StuckAfter;
        var stuck = await scope.ServiceProvider.GetRequiredService<PaperDbContext>().Orders.IgnoreQueryFilters()
            .CountAsync(o => o.Broker == BrokerCode.Alpaca && o.Purpose == OrderPurpose.Entry && (o.Status == OrderStatus.Submitting || o.Status == OrderStatus.Submitted) && o.SubmittedAt < before, cancellationToken);
        AstryxTelemetry.BrokerOrdersStuck(stuck);
        if (stuck > 0)
        {
            logger.LogWarning("{Count} Alpaca entry order(s) unresolved for more than {Minutes} minutes", stuck, (long)StuckAfter.TotalMinutes);
        }

        return stuck;
    }

    /// <summary>
    /// Alpaca is authoritative for cash (ADR-021): with nothing in flight, a difference between Alpaca's cash and the
    /// mirrored ledger (a reset in the Alpaca dashboard, a trade placed outside Astryx) is posted as an <c>adjustment</c>.
    /// Also records the connection's sync time, and marks it degraded when Alpaca stops accepting its keys.
    /// </summary>
    private async Task ReconcileAccountsAsync(CancellationToken cancellationToken)
    {
        List<Guid> connections;
        await using (var scope = services.CreateAsyncScope())
        {
            connections = await scope.ServiceProvider.GetRequiredService<PaperDbContext>().BrokerConnections.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.Status != BrokerConnectionStatus.Revoked).Select(c => c.Id).ToListAsync(cancellationToken);
        }

        foreach (var connectionId in connections)
        {
            await using var scope = services.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            if (await sp.GetRequiredService<BrokerCredentials>().OpenAsync(connectionId, cancellationToken) is not { } opened)
            {
                continue;
            }

            string? error = null;
            AlpacaAccount? account = null;
            try
            {
                account = await alpaca.GetAccountAsync(opened.Credentials, cancellationToken);
            }
            catch (AlpacaApiException ex)
            {
                error = ex.Failure == AlpacaFailure.CredentialsInvalid ? "Alpaca no longer accepts this key. Reconnect in Settings." : $"Alpaca unavailable: {ex.Failure}";
            }

            var unit = sp.GetRequiredService<ModuleUnitOfWork>();
            await unit.BeginAsync(cancellationToken);
            var db = sp.GetRequiredService<PaperDbContext>();
            var connection = await db.BrokerConnections.IgnoreQueryFilters().SingleAsync(c => c.Id == connectionId, cancellationToken);
            var now = timeProvider.GetUtcNow();
            connection.LastError = error;
            connection.Status = error is null ? BrokerConnectionStatus.Connected : BrokerConnectionStatus.Degraded;
            connection.UpdatedAt = now;
            if (account is not null)
            {
                connection.LastSyncAt = now;
                await AdjustCashAsync(sp, db, connection, account, now, cancellationToken);
                await ReconcilePositionsAsync(sp, db, connection, opened.Credentials, now, cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);
            await unit.CommitAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Alpaca is authoritative for positions too (§17.7, T-145): with nothing in flight, every symbol's quantity at Alpaca
    /// must equal our open position. A difference (a trade placed in the Alpaca dashboard, a bracket leg we missed)
    /// halts new entries until an operator has looked; exits keep working so the user can still get out.
    /// </summary>
    private async Task ReconcilePositionsAsync(IServiceProvider sp, PaperDbContext db, BrokerConnection connection, AlpacaCredentials credentials, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await sp.GetRequiredService<IPortfolioDirectory>().GetActiveAsync(connection.UserId, cancellationToken) is not { } active || active.BrokerConnectionId != connection.Id
            || await db.Orders.IgnoreQueryFilters().AnyAsync(o => o.PortfolioId == active.Id && (o.Status == OrderStatus.Submitting || o.Status == OrderStatus.Submitted), cancellationToken)
            || await sp.GetRequiredService<IPortfolioStateSource>().GetAsync(active.Id, cancellationToken) is not { ReconciliationHaltedAt: null } state)
        {
            return; // a fill is on its way, or the portfolio is already halted
        }

        IReadOnlyList<AlpacaPosition> remote;
        try
        {
            remote = await alpaca.ListPositionsAsync(credentials, cancellationToken);
        }
        catch (AlpacaApiException ex)
        {
            logger.LogWarning("Alpaca positions for connection {ConnectionId} unavailable: {Reason}", connection.Id, ex.Message);
            return;
        }

        var theirs = remote.GroupBy(p => p.Symbol, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Sum(p => p.Quantity), StringComparer.OrdinalIgnoreCase);
        var ours = state.OpenPositions.GroupBy(p => p.Symbol, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Sum(p => p.Quantity), StringComparer.OrdinalIgnoreCase);
        var issues = theirs.Keys.Union(ours.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)
            .Select(symbol => (symbol, Alpaca: theirs.GetValueOrDefault(symbol), Astryx: ours.GetValueOrDefault(symbol)))
            .Where(d => d.Alpaca != d.Astryx)
            .Select(d => $"position {d.symbol}: Alpaca holds {Shares(d.Alpaca)}, Astryx {Shares(d.Astryx)}")
            .ToList();
        if (issues.Count == 0)
        {
            return;
        }

        logger.LogCritical("Alpaca positions differ for portfolio {PortfolioId}, new entries halted: {Issues}", active.Id, string.Join(" | ", issues));
        await sp.GetRequiredService<IBrokerPortfolios>().HaltAsync(active.Id, issues, now, cancellationToken);
        db.Audit(new AuditEntry("portfolio.broker_positions_mismatch", AuditActor.System, connection.UserId, "portfolio", active.Id.ToString(), Metadata: new { issues }));
    }

    private static string Shares(decimal quantity) => quantity.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    private async Task AdjustCashAsync(IServiceProvider sp, PaperDbContext db, BrokerConnection connection, AlpacaAccount account, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await sp.GetRequiredService<IPortfolioDirectory>().GetActiveAsync(connection.UserId, cancellationToken) is not { } active || active.BrokerConnectionId != connection.Id)
        {
            return;
        }

        var inFlight = await db.Orders.IgnoreQueryFilters().AnyAsync(o => o.PortfolioId == active.Id && (o.Status == OrderStatus.Submitting || o.Status == OrderStatus.Submitted), cancellationToken);
        if (inFlight || !await sp.GetRequiredService<IPortfolioLedger>().LockAsync(active.Id, cancellationToken)
            || await sp.GetRequiredService<IPortfolioStateSource>().GetAsync(active.Id, cancellationToken) is not { } state)
        {
            return; // a fill is on its way; compare on the next pass
        }

        var drift = Astryx.SharedKernel.Finance.RoundingPolicy.Ledger(account.Cash - state.CashBalance);
        if (drift == 0m)
        {
            return;
        }

        var posted = await sp.GetRequiredService<IPortfolioLedger>().PostAsync(active.Id,
            [new LedgerEntry("adjustment", drift, $"broker_sync:{connection.Id}:{now:yyyyMMddHHmmss}", "Alpaca cash reconciliation")], cancellationToken);
        if (!posted.IsSuccess)
        {
            logger.LogError("Could not reconcile cash for portfolio {PortfolioId}: {Error}", active.Id, posted.Error.Message);
            return;
        }

        db.Audit(new AuditEntry("paper.broker_cash_adjusted", AuditActor.System, connection.UserId, "portfolio", active.Id.ToString(), Metadata: new { drift, brokerCash = account.Cash }));
    }

    /// <summary>A bracket leg (stop loss or take profit) that Alpaca filled closes the mirrored position at Alpaca's price.</summary>
    private async Task SettleExitAsync(Guid exitId, CancellationToken cancellationToken)
    {
        var (exit, credentials) = await LoadAsync(exitId, cancellationToken);
        if (exit is null || exit.Status is not (OrderStatus.Working or OrderStatus.Submitted) || credentials is null)
        {
            return; // working bracket legs, or a manual close Alpaca hadn't filled when it was sent
        }

        var remote = await alpaca.GetOrderAsync(credentials, exit.BrokerOrderId, cancellationToken);
        if (remote is not { IsFilled: true, FilledAvgPrice: { } price })
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var unit = sp.GetRequiredService<ModuleUnitOfWork>();
        await unit.BeginAsync(cancellationToken);
        if (!await sp.GetRequiredService<IPortfolioLedger>().LockAsync(exit.PortfolioId, cancellationToken))
        {
            return;
        }

        var db = sp.GetRequiredService<PaperDbContext>();
        var row = await db.Orders.IgnoreQueryFilters().SingleAsync(o => o.Id == exitId, cancellationToken);
        if (row.Status is not (OrderStatus.Working or OrderStatus.Submitted) || await sp.GetRequiredService<IPositionBook>().LockAsync(row.PositionId!.Value, cancellationToken) is not { Status: "open" })
        {
            return;
        }

        var siblings = row.OcoGroupId is { } group
            ? await db.Orders.IgnoreQueryFilters().Where(o => o.PositionId == row.PositionId && o.OcoGroupId == group).ToListAsync(cancellationToken)
            : await db.Orders.IgnoreQueryFilters().Where(o => o.PositionId == row.PositionId && o.OcoGroupId != null).ToListAsync(cancellationToken);
        var reference = row.StopPrice ?? row.LimitPrice ?? price;
        var isStop = row.Purpose == OrderPurpose.ExitStop;
        var reason = row.Purpose switch { OrderPurpose.ExitStop => "stop", OrderPurpose.ExitTarget => "target", _ => "manual" };
        var closed = await sp.GetRequiredService<PaperExecution>().ExitAsync(
            row, siblings, price, reference, Math.Max(0m, reference - price), reason, gapped: isStop && price < reference,
            remote.FilledAt ?? timeProvider.GetUtcNow(), remote.FilledAt ?? timeProvider.GetUtcNow(), cancellationToken);
        if (!closed.IsSuccess)
        {
            logger.LogError("Alpaca filled exit {OrderId} but it could not be recorded: {Error}", exitId, closed.Error.Message);
            return;
        }

        await db.SaveChangesAsync(cancellationToken);
        await unit.CommitAsync(cancellationToken);
    }

    private async Task RecordEntryFillAsync(Guid orderId, AlpacaOrder remote, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var unit = sp.GetRequiredService<ModuleUnitOfWork>();
        await unit.BeginAsync(cancellationToken);
        var db = sp.GetRequiredService<PaperDbContext>();
        var order = await db.Orders.IgnoreQueryFilters().SingleAsync(o => o.Id == orderId, cancellationToken);
        if (!await sp.GetRequiredService<IPortfolioLedger>().LockAsync(order.PortfolioId, cancellationToken))
        {
            return;
        }

        await db.Entry(order).ReloadAsync(cancellationToken);
        if (order.Status is not (OrderStatus.Submitted or OrderStatus.Working or OrderStatus.Submitting))
        {
            return; // settled by a concurrent pass
        }

        if (order.ReservedAmount > 0)
        {
            var released = await sp.GetRequiredService<IPortfolioLedger>().ReserveAsync(order.PortfolioId, -order.ReservedAmount, cancellationToken);
            if (!released.IsSuccess)
            {
                logger.LogError("Could not release the reservation of order {OrderId}: {Error}", orderId, released.Error.Message);
                return;
            }

            order.ReservedAmount = 0;
        }

        var price = remote.FilledAvgPrice!.Value;
        var reference = order.StopPrice ?? order.LimitPrice ?? price;
        order.Quantity = remote.FilledQuantity; // a partial fill that ended at the close becomes the position size
        if (order.BrokerOrderId != remote.Id)
        {
            order.BrokerOrderId = remote.Id;
        }

        var legs = BracketLegs(remote);
        var levels = await sp.GetRequiredService<IExecutionGate>().LevelsAsync(order.ProposalId!.Value, cancellationToken);
        var filledAt = remote.FilledAt ?? timeProvider.GetUtcNow();
        var filled = await sp.GetRequiredService<PaperExecution>().FillEntryAsync(
            order, new SimulatedFill(price, reference, Math.Max(0m, price - reference)), levels.Stop, levels.Target, levels.StrategyVersionId, filledAt, filledAt, cancellationToken, legs);
        if (!filled.IsSuccess)
        {
            // Alpaca holds the shares; the nightly reconciliation halts the portfolio until someone looks (§20.13).
            logger.LogError("Alpaca filled order {OrderId} but it could not be mirrored: {Error}", orderId, filled.Error.Message);
            return;
        }

        AstryxTelemetry.Orders.Add(1, new("purpose", "entry"), new("outcome", "filled"));
        db.Audit(new AuditEntry("paper.broker_fill", AuditActor.System, order.UserId, "order", order.Id.ToString(), Metadata: new
        {
            broker = BrokerCode.Alpaca,
            brokerOrderId = remote.Id,
            quantity = remote.FilledQuantity,
            price,
        }));
        await db.SaveChangesAsync(cancellationToken);
        await unit.CommitAsync(cancellationToken);
    }

    private async Task CloseUnfilledAsync(Guid orderId, string status, string code, string reason, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var unit = sp.GetRequiredService<ModuleUnitOfWork>();
        await unit.BeginAsync(cancellationToken);
        var db = sp.GetRequiredService<PaperDbContext>();
        var order = await db.Orders.IgnoreQueryFilters().SingleAsync(o => o.Id == orderId, cancellationToken);
        if (!await sp.GetRequiredService<IPortfolioLedger>().LockAsync(order.PortfolioId, cancellationToken))
        {
            return;
        }

        await db.Entry(order).ReloadAsync(cancellationToken);
        if (order.Status is not (OrderStatus.Submitting or OrderStatus.Submitted or OrderStatus.Working))
        {
            return;
        }

        if (order.ReservedAmount > 0)
        {
            var released = await sp.GetRequiredService<IPortfolioLedger>().ReserveAsync(order.PortfolioId, -order.ReservedAmount, cancellationToken);
            if (!released.IsSuccess)
            {
                logger.LogError("Could not release the reservation of order {OrderId}: {Error}", orderId, released.Error.Message);
                return;
            }

            order.ReservedAmount = 0;
        }

        order.Status = status;
        order.RejectCode = code.Length > 64 ? code[..64] : code;
        order.CancelledAt = timeProvider.GetUtcNow();
        order.Version++;
        db.Publish(new OrderRejected(order.UserId, order.Id, order.ProposalId, order.Symbol, status, reason));
        AstryxTelemetry.Orders.Add(1, new("purpose", "entry"), new("outcome", status));
        db.Audit(new AuditEntry("paper.broker_order_closed", AuditActor.System, order.UserId, "order", order.Id.ToString(), Metadata: new { status, code }));
        await db.SaveChangesAsync(cancellationToken);
        await unit.CommitAsync(cancellationToken);
    }

    private async Task<(Order? Order, AlpacaCredentials? Credentials)> LoadAsync(Guid orderId, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaperDbContext>();
        var order = await db.Orders.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId && o.Broker == BrokerCode.Alpaca, cancellationToken);
        if (order is null)
        {
            return (null, null);
        }

        var connectionId = await db.BrokerConnections.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.UserId == order.UserId && c.Broker == BrokerCode.Alpaca && c.Status != BrokerConnectionStatus.Revoked)
            .Select(c => (Guid?)c.Id).FirstOrDefaultAsync(cancellationToken);
        var opened = connectionId is { } id ? await scope.ServiceProvider.GetRequiredService<BrokerCredentials>().OpenAsync(id, cancellationToken) : null;
        return (order, opened?.Credentials);
    }

    private static BrokerLegs? BracketLegs(AlpacaOrder entry)
    {
        var stop = entry.Legs.FirstOrDefault(l => l.Type is "stop" or "stop_limit");
        var target = entry.Legs.FirstOrDefault(l => l.Type == "limit");
        return stop is not null && target is not null ? new BrokerLegs(stop.Id, target.Id) : null;
    }

    /// <summary>The order id: unique per intent, well under Alpaca's 128 characters, and stable across retries.</summary>
    public static string ClientId(Order order) => order.Id.ToString();

    private static string AlpacaType(string orderType) => orderType switch
    {
        "buy_stop" => "stop",
        "limit" => "limit",
        _ => "market",
    };

    private static string RejectCode(AlpacaFailure failure) => failure switch
    {
        AlpacaFailure.InsufficientFunds => "broker_insufficient_funds",
        AlpacaFailure.CredentialsInvalid or AlpacaFailure.Forbidden => "broker_credentials_invalid",
        _ => "broker_order_invalid",
    };
}

public sealed class SubmitBrokerOrderHandler(AlpacaExecution execution) : IJobHandler<SubmitBrokerOrderJob>
{
    public Task HandleAsync(SubmitBrokerOrderJob job, JobContext context, CancellationToken cancellationToken) => execution.SubmitAsync(job.OrderId, cancellationToken);
}

public sealed class SettleBrokerOrderHandler(AlpacaExecution execution) : IJobHandler<SettleBrokerOrderJob>
{
    public Task HandleAsync(SettleBrokerOrderJob job, JobContext context, CancellationToken cancellationToken) => execution.SettleWithBackoffAsync(job.OrderId, job.Attempt, cancellationToken);
}

public sealed class BrokerSyncHandler(AlpacaExecution execution) : IJobHandler<BrokerSyncJob>
{
    public Task HandleAsync(BrokerSyncJob job, JobContext context, CancellationToken cancellationToken) => execution.SyncAsync(cancellationToken);
}
