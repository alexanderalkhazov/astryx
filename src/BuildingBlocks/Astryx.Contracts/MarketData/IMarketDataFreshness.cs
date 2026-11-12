namespace Astryx.Contracts.MarketData;

/// <summary>How new the stored market data is (SYSTEM_DESIGN.md §20.5): the newest quote and the last final daily session.</summary>
public sealed record MarketFreshness(DateTimeOffset? QuotesAsOf, DateOnly? DailyBarsThrough, bool QuotesStale);

/// <summary>Owned by MarketData; the scanner runs when this moves on (new quotes in session, new bars after the close).</summary>
public interface IMarketDataFreshness
{
    Task<MarketFreshness> GetAsync(CancellationToken cancellationToken);
}
