using Astryx.Contracts.MarketData;

namespace Astryx.Modules.MarketData.Application.Market;

/// <summary><see cref="IMarketDataFreshness"/> from the same figures as <c>/system/status</c>.</summary>
public sealed class MarketDataFreshness(FreshnessPolicy freshness) : IMarketDataFreshness
{
    public async Task<MarketFreshness> GetAsync(CancellationToken cancellationToken)
    {
        var status = await freshness.StatusAsync(cancellationToken);
        return new MarketFreshness(status.Quotes.AsOf, status.DailyBarsThrough, status.Quotes.Stale);
    }
}
