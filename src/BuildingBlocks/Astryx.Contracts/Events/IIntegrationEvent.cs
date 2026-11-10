namespace Astryx.Contracts.Events;

/// <summary>
/// An event published by one module and consumed by others through the transactional outbox (SYSTEM_DESIGN.md §9.4).
/// <see cref="EventName"/> is persisted with each message, so it must stay stable once released: version it
/// (<c>identity.user_registered.v1</c>) instead of renaming.
/// </summary>
public interface IIntegrationEvent
{
    static abstract string EventName { get; }
}
