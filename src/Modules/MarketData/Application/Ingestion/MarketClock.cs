using Astryx.Modules.MarketData.Application.Providers;
using Astryx.Modules.MarketData.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NodaTime;

namespace Astryx.Modules.MarketData.Application.Ingestion;

/// <summary>Exchange-time helpers for the sync schedules: New York wall-clock times and US equity sessions.</summary>
public sealed class MarketClock(MarketDataDbContext db, IMarketDataProvider provider, TimeProvider timeProvider, IMemoryCache cache)
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

    /// <summary>A stored calendar row is reused this long: one request asks for today's session a dozen times (T-116).</summary>
    private static readonly TimeSpan CalendarCacheTtl = TimeSpan.FromMinutes(10L);

    /// <summary>The session row for a date, from <c>market.trading_calendar</c> or, before the first calendar sync, the provider.</summary>
    public async Task<ProviderSession?> SessionAsync(DateOnly date, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(("trading-calendar", date), out ProviderSession? cached))
        {
            return cached;
        }

        var stored = await db.TradingCalendar.AsNoTracking().SingleOrDefaultAsync(s => s.ExchangeMic == PrimaryMic && s.SessionDate == date, cancellationToken);
        if (stored is not null)
        {
            // Only stored rows are cached: the provider fallback below must be retried until the calendar sync has run.
            return cache.Set(("trading-calendar", date), new ProviderSession(stored.ExchangeMic, stored.SessionDate, stored.OpenAt, stored.CloseAt, stored.IsHalfDay, stored.IsHoliday), CalendarCacheTtl);
        }

        try
        {
            return (await provider.GetCalendarAsync(PrimaryMic, date, date, cancellationToken)).SingleOrDefault();
        }
        catch (MarketDataUnavailableException)
        {
            // Best effort without the provider: weekdays 09:30–16:00 ET, holidays unknown. Stored data must keep answering.
            return date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
                ? null
                : new ProviderSession(PrimaryMic, date, At(date, 9, 30), At(date, 16, 0), IsHalfDay: false, IsHoliday: false);
        }
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

    /// <summary>The first trading session on or after <paramref name="from"/> (holidays and weekends skipped; looks two weeks ahead).</summary>
    public async Task<ProviderSession?> NextSessionAsync(DateOnly from, CancellationToken cancellationToken)
    {
        for (var date = from; date <= from.AddDays(14); date = date.AddDays(1))
        {
            if (await SessionAsync(date, cancellationToken) is { IsHoliday: false } session)
            {
                return session;
            }
        }

        return null;
    }

    /// <summary>The most recent session that has closed by now.</summary>
    public async Task<ProviderSession?> LatestClosedSessionAsync(CancellationToken cancellationToken)
    {
        for (var date = Today; date >= Today.AddDays(-14); date = date.AddDays(-1))
        {
            if (await SessionAsync(date, cancellationToken) is { IsHoliday: false } session && session.CloseAt <= Now)
            {
                return session;
            }
        }

        return null;
    }

    /// <summary>New York wall-clock <paramref name="hour"/>:<paramref name="minute"/> on <paramref name="date"/>, in UTC.</summary>
    public static DateTimeOffset At(DateOnly date, int hour, int minute) =>
        new LocalDate(date.Year, date.Month, date.Day).At(new LocalTime(hour, minute)).InZoneLeniently(NewYork).ToDateTimeOffset().ToUniversalTime();

    private static DateOnly ToDate(LocalDate date) => new(date.Year, date.Month, date.Day);
}
