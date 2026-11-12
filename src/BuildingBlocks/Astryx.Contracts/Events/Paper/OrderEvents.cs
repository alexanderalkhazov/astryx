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

/// <summary>A position closed by its stop, its target or the user.</summary>
/// <param name="GappedThroughStop">The exit filled below the stop (a gap): the loss exceeds the planned maximum.</param>
public sealed record PositionClosed(Guid UserId, Guid PositionId, string Symbol, string ExitReason, decimal ExitPrice, decimal RealizedPnl, bool GappedThroughStop) : IIntegrationEvent
{
    public static string EventName => "paper.position_closed.v1";
}

/// <summary>A working day order expired at the session close; its reserved cash was released.</summary>
public sealed record OrderExpired(Guid UserId, Guid OrderId, Guid? ProposalId, string Symbol) : IIntegrationEvent
{
    public static string EventName => "paper.order_expired.v1";
}

/// <summary>The last price came within 1.5% of a position's stop (once per position per session).</summary>
public sealed record StopProximityReached(Guid UserId, Guid PositionId, string Symbol, decimal Last, decimal Stop) : IIntegrationEvent
{
    public static string EventName => "paper.stop_proximity.v1";
}
