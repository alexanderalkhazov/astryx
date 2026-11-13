namespace Astryx.Contracts.Events.Strategies;

/// <summary>
/// A system strategy's drawdown guard changed its status (SYSTEM_DESIGN.md §7.8): <c>active_guarded</c> (warning),
/// <c>paused</c> (new signals stop) or back to <c>active</c>. Global, not per user.
/// </summary>
/// <param name="Drawdown">From the window's high, as a fraction (−0.054).</param>
/// <param name="PausePct">The pause threshold, as a fraction (−0.06).</param>
public sealed record StrategyGuardChanged(
    Guid StrategyId, string Code, string Name, string PreviousStatus, string Status, decimal Drawdown, decimal PausePct, int WindowDays, DateOnly AsOf) : IIntegrationEvent
{
    public static string EventName => "strategies.guard_changed.v1";
}
