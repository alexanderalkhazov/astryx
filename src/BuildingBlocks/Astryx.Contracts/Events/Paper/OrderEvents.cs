namespace Astryx.Contracts.Events.Paper;

/// <summary>An entry order was accepted: filled at once or working (SYSTEM_DESIGN.md §9.6 <c>order.updated</c>).</summary>
public sealed record OrderAccepted(Guid UserId, Guid OrderId, Guid? ProposalId, string Symbol, string Status) : IIntegrationEvent
{
    public static string EventName => "paper.order_accepted.v1";
}

/// <summary>An order filled (entry or exit).</summary>
public sealed record OrderFilled(Guid UserId, Guid OrderId, Guid? PositionId, string Symbol, string Purpose, decimal Quantity, decimal Price, DateTimeOffset FilledAt) : IIntegrationEvent
{
    public static string EventName => "paper.order_filled.v1";
}

/// <summary>A position opened from an entry fill.</summary>
public sealed record PositionOpened(Guid UserId, Guid PositionId, Guid PortfolioId, string Symbol, decimal Quantity, decimal AvgEntryPrice) : IIntegrationEvent
{
    public static string EventName => "paper.position_opened.v1";
}
