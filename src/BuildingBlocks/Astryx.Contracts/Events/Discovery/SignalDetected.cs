namespace Astryx.Contracts.Events.Discovery;

/// <summary>
/// A new active signal: an instrument and strategy that had no active signal before this scanner run
/// (SYSTEM_DESIGN.md §7.4). Feeds Activity "New signal"; re-confirming an existing signal does not publish again.
/// </summary>
public sealed record SignalDetected(
    Guid SignalId, Guid InstrumentId, string Symbol, Guid StrategyId, string StrategyName, string SignalType, decimal Score, string Reason, DateTimeOffset EvaluatedAt)
    : IIntegrationEvent
{
    public static string EventName => "discovery.signal_detected.v1";
}
