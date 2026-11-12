using Astryx.Modules.MarketData.Application.Providers;
using Astryx.Modules.MarketData.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Astryx.Modules.MarketData.Application.Ingestion;

/// <summary>Exchange-time helpers for the sync schedules: New York wall-clock times and US equity sessions.</summary>
public sealed class MarketClock(MarketDataDbContext db, IMarketDataProvider provider, TimeProvider timeProvider)
{
    public const string PrimaryMic = "XNYS";

    private static readonly DateTimeZone NewYork = DateTimeZoneProviders.Tzdb["America/New_York"];

    public DateTimeOffset Now => timeProvider.GetUtcNow();

    /// <summary>Today's date in New York.</summary>
    public DateOnly Today => ToDate(Instant.FromDateTimeOffset(Now).InZone(NewYork).Date);

    /// <summary>The latest instant at <paramref name="hour"/>:<paramref name="minute"/> New York time that is not after now.</summary>
    public DateTimeOffset LatestDaily(int hour, int minute)
    {
        var local = Instant.FromDateTimeOffset(Now).InZone(NewYork);
        var candidate = local.Date.At(new LocalTime(hour, minute)).InZoneLeniently(NewYork);
        if (candidate.ToInstant() > local.ToInstant())
        {
            candidate = local.Date.PlusDays(-1).At(new LocalTime(hour, minute)).InZoneLeniently(NewYork);
        }

        return candidate.ToDateTimeOffset().ToUniversalTime();
    }

    /// <summary>The session row for a date, from <c>market.trading_calendar</c> or, before the first calendar sync, the provider.</summary>
    public async Task<ProviderSession?> SessionAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var stored = await db.TradingCalendar.AsNoTracking().SingleOrDefaultAsync(s => s.ExchangeMic == PrimaryMic && s.SessionDate == date, cancellationToken);
        if (stored is not null)
        {
            return new ProviderSession(stored.ExchangeMic, stored.SessionDate, stored.OpenAt, stored.CloseAt, stored.IsHalfDay, stored.IsHoliday);
        }

        return (await provider.GetCalendarAsync(PrimaryMic, date, date, cancellationToken)).SingleOrDefault();
    }

    /// <summary>The most recent trading session that has opened (today's if it is under way or done).</summary>
    public async Task<ProviderSession> CurrentSessionAsync(CancellationToken cancellationToken)
    {
        for (var date = Today; ; date = date.AddDays(-1))
        {
            if (await SessionAsync(date, cancellationToken) is { IsHoliday: false } session && session.OpenAt <= Now)
            {
                return session;
            }
        }
    }

    private static DateOnly ToDate(LocalDate date) => new(date.Year, date.Month, date.Day);
}
