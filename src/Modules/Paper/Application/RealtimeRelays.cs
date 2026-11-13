using Astryx.Contracts.Events;
using Astryx.Contracts.Events.Paper;
using Astryx.Infrastructure.Realtime;

namespace Astryx.Modules.Paper.Application;

/// <summary><c>order.updated</c> payload (SYSTEM_DESIGN.md §9.6). Quantity and price are set when it filled.</summary>
public sealed record OrderUpdatedMessage(Guid OrderId, Guid? ProposalId, string Symbol, string Status, string Purpose, decimal? Quantity, decimal? Price);

/// <summary><c>position.updated</c> payload (§9.6).</summary>
public sealed record PositionUpdatedMessage(Guid PositionId, string Symbol, string Status, string? ExitReason, decimal? Price, decimal? RealizedPnl, bool GappedThroughStop);

/// <summary>
/// Relays paper events from the outbox to the user's devices (§9.6). Pushes are hints: clients refetch REST, so a
/// re-delivered event only repeats a refresh.
/// </summary>
public sealed class PaperRealtimeRelays(IRealtimeNotifier notifier) :
    IIntegrationEventHandler<OrderAccepted>,
    IIntegrationEventHandler<OrderFilled>,
    IIntegrationEventHandler<OrderExpired>,
    IIntegrationEventHandler<PositionOpened>,
    IIntegrationEventHandler<PositionClosed>
{
    public const string OrderUpdated = "order.updated";
    public const string PositionUpdated = "position.updated";

    public Task HandleAsync(OrderAccepted e, IntegrationEventContext context, CancellationToken cancellationToken) =>
        notifier.SendToUserAsync(e.UserId, OrderUpdated, new OrderUpdatedMessage(e.OrderId, e.ProposalId, e.Symbol, e.Status, "entry", null, null), cancellationToken);

    public Task HandleAsync(OrderFilled e, IntegrationEventContext context, CancellationToken cancellationToken) =>
        notifier.SendToUserAsync(e.UserId, OrderUpdated, new OrderUpdatedMessage(e.OrderId, null, e.Symbol, "filled", e.Purpose, e.Quantity, e.Price), cancellationToken);

    public Task HandleAsync(OrderExpired e, IntegrationEventContext context, CancellationToken cancellationToken) =>
        notifier.SendToUserAsync(e.UserId, OrderUpdated, new OrderUpdatedMessage(e.OrderId, e.ProposalId, e.Symbol, "expired", "entry", null, null), cancellationToken);

    public Task HandleAsync(PositionOpened e, IntegrationEventContext context, CancellationToken cancellationToken) =>
        notifier.SendToUserAsync(e.UserId, PositionUpdated, new PositionUpdatedMessage(e.PositionId, e.Symbol, "open", null, e.AvgEntryPrice, null, false), cancellationToken);

    public Task HandleAsync(PositionClosed e, IntegrationEventContext context, CancellationToken cancellationToken) =>
        notifier.SendToUserAsync(e.UserId, PositionUpdated, new PositionUpdatedMessage(e.PositionId, e.Symbol, "closed", e.ExitReason, e.ExitPrice, e.RealizedPnl, e.GappedThroughStop), cancellationToken);
}
