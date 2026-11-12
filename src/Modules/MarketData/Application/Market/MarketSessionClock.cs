using Astryx.Contracts.MarketData;
using Astryx.Modules.MarketData.Application.Ingestion;

namespace Astryx.Modules.MarketData.Application.Market;

/// <summary><see cref="IMarketSessionClock"/> from the stored trading calendar (provider fallback in <see cref="MarketClock"/>).</summary>
public sealed class MarketSessionClock(MarketClock clock) : IMarketSessionClock
{
    public DateTimeOffset Now => clock.Now;

    public async Task<TradingSession?> TodayAsync(CancellationToken cancellationToken) =>
        await clock.SessionAsync(clock.Today, cancellationToken) is { IsHoliday: false, OpenAt: { } open, CloseAt: { } close } s
            ? new TradingSession(s.SessionDate, open, close, s.IsHalfDay)
            : null;
}
