using System.Diagnostics;
using System.Text.Json;
using Astryx.Contracts.Events;
using Astryx.Infrastructure.Observability;
using Astryx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astryx.Infrastructure.Outbox;

/// <summary>
/// Delivers committed outbox messages to in-process handlers (SYSTEM_DESIGN.md §9.4, §19.2).
/// <list type="bullet">
/// <item>Claims a batch with <c>FOR UPDATE SKIP LOCKED</c>, so concurrent dispatchers never take the same message.</item>
/// <item>Records each successful handler in <c>infra.outbox_deliveries</c>; a retry only re-runs the handlers that failed.</item>
/// <item>Retries with exponential backoff; marks the message poison after <see cref="OutboxOptions.MaxAttempts"/>.</item>
/// </list>
/// Delivery is at-least-once (a crash after a handler succeeds but before the batch commits re-runs that handler),
/// which is why handlers must be idempotent.
/// </summary>
public sealed class OutboxDispatcher(
    IServiceProvider services,
    IntegrationEventRegistry registry,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcher> logger)
{
    /// <summary>Processes one batch. Returns the number of messages claimed.</summary>
    public async Task<int> DispatchOnceAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();
        var batch = await db.OutboxMessages
            .FromSql($"""
                SELECT * FROM infra.outbox_messages
                WHERE processed_at IS NULL AND failed_at IS NULL AND next_attempt_at <= {now}
                ORDER BY occurred_at, id
                LIMIT {settings.BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);
        if (batch.Count == 0)
        {
            return 0;
        }

        var ids = batch.Select(m => m.Id).ToList();
        var delivered = (await db.OutboxDeliveries.Where(d => ids.Contains(d.MessageId)).Select(d => new { d.MessageId, d.Handler }).ToListAsync(cancellationToken))
            .Select(d => (d.MessageId, d.Handler))
            .ToHashSet();

        foreach (var message in batch)
        {
            await DispatchMessageAsync(db, message, delivered, settings, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return batch.Count;
    }

    private async Task DispatchMessageAsync(
        InfraDbContext db, OutboxMessage message, HashSet<(Guid, string)> delivered, OutboxOptions settings, CancellationToken cancellationToken)
    {
        var context = new IntegrationEventContext(message.Id, message.OccurredAt, message.CorrelationId);
        var previousCorrelation = CorrelationContext.Id;
        CorrelationContext.Id = message.CorrelationId;
        using var logScope = logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = message.CorrelationId, ["OutboxMessageId"] = message.Id });
        using var activity = AstryxTelemetry.Source.StartActivity($"outbox {message.Type}", ActivityKind.Consumer);
        activity?.SetTag("astryx.correlation_id", message.CorrelationId);
        activity?.SetTag("astryx.outbox_message_id", message.Id);
        AstryxTelemetry.OutboxLag(timeProvider.GetUtcNow() - message.OccurredAt);
        try
        {
            foreach (var handler in registry.HandlersFor(message.Type))
            {
                if (delivered.Contains((message.Id, handler.HandlerName)))
                {
                    continue;
                }

                await using var handlerScope = services.CreateAsyncScope();
                await handler.InvokeAsync(handlerScope.ServiceProvider, message.Payload, context, cancellationToken);
                db.OutboxDeliveries.Add(new OutboxDelivery { MessageId = message.Id, Handler = handler.HandlerName, DeliveredAt = timeProvider.GetUtcNow() });
            }

            message.ProcessedAt = timeProvider.GetUtcNow();
        }
        catch (JsonException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "poison");
            message.Attempts++;
            message.LastError = Truncate($"Undeserialisable payload: {ex.Message}");
            message.FailedAt = timeProvider.GetUtcNow();
            logger.LogError(ex, "Outbox message {MessageId} ({Type}) is poison: payload cannot be deserialised", message.Id, message.Type);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            message.Attempts++;
            message.LastError = Truncate($"{ex.GetType().Name}: {ex.Message}");
            if (message.Attempts >= settings.MaxAttempts)
            {
                message.FailedAt = timeProvider.GetUtcNow();
                logger.LogError(ex, "Outbox message {MessageId} ({Type}) is poison after {Attempts} attempts", message.Id, message.Type, message.Attempts);
            }
            else
            {
                message.NextAttemptAt = timeProvider.GetUtcNow() + Backoff(message.Attempts, settings);
                logger.LogWarning(ex, "Outbox message {MessageId} ({Type}) failed attempt {Attempts}; retrying at {NextAttemptAt}", message.Id, message.Type, message.Attempts, message.NextAttemptAt);
            }
        }
        finally
        {
            CorrelationContext.Id = previousCorrelation;
        }
    }

    internal static TimeSpan Backoff(int attempts, OutboxOptions settings)
    {
        var exponent = Math.Min(attempts - 1, 20);
        var delay = settings.BaseBackoff * (1L << exponent);
        return delay > settings.MaxBackoff ? settings.MaxBackoff : delay;
    }

    private static string Truncate(string value) => value.Length <= 2000 ? value : value[..2000];
}
