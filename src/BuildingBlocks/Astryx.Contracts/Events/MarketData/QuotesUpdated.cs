namespace Astryx.Contracts.Events.MarketData;

/// <summary>
/// Quotes that changed in one sync cycle (SYSTEM_DESIGN.md §16.3). One batched event per cycle rather than one per
/// instrument keeps the outbox at a row a minute instead of ~1,000; consumers (PositionMonitor) iterate the batch.
/// </summary>
public sealed record QuotesUpdated(IReadOnlyList<QuoteChange> Quotes) : IIntegrationEvent
{
    public static string EventName => "market.quotes_updated.v1";
}

public sealed record QuoteChange(Guid InstrumentId, string Symbol, decimal Last, DateTimeOffset AsOf, long QuoteSeq);
