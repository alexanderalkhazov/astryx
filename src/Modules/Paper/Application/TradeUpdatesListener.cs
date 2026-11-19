using System.Collections.Concurrent;
using Astryx.Infrastructure.Alpaca;
using Astryx.Infrastructure.Observability;
using Astryx.Modules.Paper.Domain;
using Astryx.Modules.Paper.Infrastructure.Alpaca;
using Astryx.Modules.Paper.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Astryx.Modules.Paper.Application;

/// <summary>
/// Alpaca's <c>trade_updates</c> stream as the fast path for settlement (SYSTEM_DESIGN.md §17.7, TASKS.md T-148): one
/// reconnecting listener per active connection, and every final event (fill, partial fill, cancel, expiry, rejection)
/// settles its order at once through the same lookup the one-minute <c>BrokerSyncJob</c> uses. The stream is never the
/// only source: a missed event is settled by the next sync.
/// </summary>
public sealed class TradeUpdatesListener(IServiceProvider services, IAlpacaTradeUpdates stream, AlpacaExecution execution, TimeProvider timeProvider, ILogger<TradeUpdatesListener> logger)
    : BackgroundService
{
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(30L);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60L);
    private static readonly HashSet<string> Settling = ["fill", "partial_fill", "canceled", "expired", "rejected", "done_for_day"];

    private readonly ConcurrentDictionary<Guid, (Task Task, CancellationTokenSource Stop)> _listeners = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not refresh the Alpaca stream listeners");
            }

            try
            {
                await Task.Delay(RefreshEvery, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        foreach (var (_, listener) in _listeners)
        {
            await listener.Stop.CancelAsync();
        }

        await Task.WhenAll(_listeners.Values.Select(l => l.Task));
    }

    /// <summary>Starts a listener for each active connection and stops the ones whose connection went away.</summary>
    private async Task RefreshAsync(CancellationToken stoppingToken)
    {
        List<Guid> active;
        await using (var scope = services.CreateAsyncScope())
        {
            active = await scope.ServiceProvider.GetRequiredService<PaperDbContext>().BrokerConnections.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.Status == BrokerConnectionStatus.Connected).Select(c => c.Id).ToListAsync(stoppingToken);
        }

        foreach (var gone in _listeners.Keys.Except(active).ToList())
        {
            if (_listeners.TryRemove(gone, out var listener))
            {
                await listener.Stop.CancelAsync();
            }
        }

        foreach (var connectionId in active.Where(id => !_listeners.ContainsKey(id) || _listeners[id].Task.IsCompleted))
        {
            var stop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            _listeners[connectionId] = (ListenAsync(connectionId, stop.Token), stop);
        }
    }

    /// <summary>Listens for one connection until stopped, reconnecting with backoff (1 s doubling to 60 s, reset by any event).</summary>
    private async Task ListenAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        var backoff = TimeSpan.FromSeconds(1L);
        while (!cancellationToken.IsCancellationRequested)
        {
            AlpacaCredentials? credentials;
            await using (var scope = services.CreateAsyncScope())
            {
                credentials = (await scope.ServiceProvider.GetRequiredService<BrokerCredentials>().OpenAsync(connectionId, cancellationToken))?.Credentials;
            }

            if (credentials is null)
            {
                return; // revoked or unreadable: the next refresh decides
            }

            try
            {
                logger.LogInformation("Listening to Alpaca trade_updates for connection {ConnectionId}", connectionId);
                await foreach (var update in stream.ListenAsync(credentials, cancellationToken))
                {
                    backoff = TimeSpan.FromSeconds(1L);
                    await HandleAsync(update, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (AlpacaApiException ex) when (ex.Failure == AlpacaFailure.CredentialsInvalid)
            {
                logger.LogWarning("Alpaca stream refused connection {ConnectionId}'s key; the sync marks it degraded", connectionId);
                return;
            }
            catch (Exception ex)
            {
                logger.LogInformation("Alpaca stream for connection {ConnectionId} dropped ({Reason}); reconnecting in {Seconds} s", connectionId, ex.Message, (long)backoff.TotalSeconds);
            }

            try
            {
                await Task.Delay(backoff, timeProvider, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            backoff = backoff * 2 > MaxBackoff ? MaxBackoff : backoff * 2;
        }
    }

    /// <summary>Settles the order an event is about. Public for tests, which feed events without a socket.</summary>
    public async Task HandleAsync(AlpacaTradeUpdate update, CancellationToken cancellationToken)
    {
        if (!Settling.Contains(update.Event))
        {
            return;
        }

        Guid? orderId;
        string? purpose;
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PaperDbContext>();
            var byBroker = await db.Orders.IgnoreQueryFilters().AsNoTracking()
                .Where(o => o.Broker == BrokerCode.Alpaca && o.BrokerOrderId == update.OrderId).Select(o => new { o.Id, o.Purpose }).FirstOrDefaultAsync(cancellationToken);

            // An entry still `submitting` has no Alpaca id yet; its client_order_id is our order id.
            var byClient = byBroker is null && Guid.TryParse(update.ClientOrderId, out var clientId)
                ? await db.Orders.IgnoreQueryFilters().AsNoTracking().Where(o => o.Broker == BrokerCode.Alpaca && o.Id == clientId).Select(o => new { o.Id, o.Purpose }).FirstOrDefaultAsync(cancellationToken)
                : null;
            var found = byBroker ?? byClient;
            (orderId, purpose) = (found?.Id, found?.Purpose);
        }

        if (orderId is not { } id)
        {
            return; // not ours (placed in the Alpaca dashboard): reconciliation reports it
        }

        try
        {
            if (purpose == OrderPurpose.Entry)
            {
                await execution.SettleAsync(id, cancellationToken);
            }
            else
            {
                await execution.SettleExitAsync(id, cancellationToken);
            }
        }
        catch (AlpacaApiException ex)
        {
            logger.LogWarning("Settling order {OrderId} from a {Event} event failed ({Reason}); the sync retries", id, update.Event, ex.Message);
            return;
        }

        if (update.At is { } at)
        {
            AstryxTelemetry.BrokerTradeUpdateLag(timeProvider.GetUtcNow() - at, update.Event);
        }
    }
}
