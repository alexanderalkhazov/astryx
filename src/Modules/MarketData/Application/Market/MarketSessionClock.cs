using Astryx.Contracts.MarketData;
using Astryx.Modules.MarketData.Application.Ingestion;
using Astryx.Modules.MarketData.Infrastructure.Providers.Fake;

namespace Astryx.Modules.MarketData.Application.Market;

/// <summary><see cref="IMarketSessionClock"/> from the stored trading calendar (provider fallback in <see cref="MarketClock"/>).</summary>
/// The Development-only session override (<see cref="MarketDataFaults.SessionForcedOpen"/>) replaces the calendar for E2E.
public sealed class MarketSessionClock(MarketClock clock, MarketDataFaults faults) : IMarketSessionClock
{
    public DateTimeOffset Now => clock.Now;

    public async Task<TradingSession?> TodayAsync(CancellationToken cancellationToken)
    {
        if (faults.SessionForcedOpen)
        {
            return new TradingSession(clock.Today, clock.Now.AddHours(-1), clock.Now.AddHours(3), false);
        }

        return await clock.SessionAsync(clock.Today, cancellationToken) is { IsHoliday: false, OpenAt: { } open, CloseAt: { } close } s
            ? new TradingSession(s.SessionDate, open, close, s.IsHalfDay)
            : null;
    }
}
