namespace Astryx.Contracts.Events.MarketData;

/// <summary>
/// A split or dividend was stored or changed (SYSTEM_DESIGN.md §20.7): adjusted history before its ex-date is now
/// different, so anything cached from it (Quant's indicator snapshots) must be recomputed.
/// </summary>
public sealed record CorporateActionRecorded(Guid InstrumentId, string Symbol, string ActionType, DateOnly ExDate) : IIntegrationEvent
{
    public static string EventName => "market.corporate_action_recorded.v1";
}
