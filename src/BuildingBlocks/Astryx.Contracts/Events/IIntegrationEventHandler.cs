namespace Astryx.Contracts.Events;

/// <summary>
/// Handles an integration event. Delivery is at-least-once: a handler may see the same message again after a crash,
/// so it must be idempotent, e.g. by deduplicating on <see cref="IntegrationEventContext.MessageId"/>.
/// </summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken);
}

public sealed record IntegrationEventContext(Guid MessageId, DateTimeOffset OccurredAt, string? CorrelationId);
