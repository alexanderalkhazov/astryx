using Astryx.Modules.MarketData.Application.Providers;
using NodaTime;

namespace Astryx.Modules.MarketData.Infrastructure.Providers.Fake;

/// <summary>
/// US equity sessions: weekdays 09:30–16:00 New York time (13:00 on half days), holidays from the fixture. DST comes
/// from NodaTime's bundled tzdb, so the container needs no OS time-zone data. Years outside the fixture have no holidays.
/// </summary>
internal sealed class FakeCalendar(FixtureData fixtures)
{
    public static readonly IReadOnlySet<string> UsEquityMics = new HashSet<string>(StringComparer.Ordinal) { "XNYS", "XNAS", "ARCX" };

    private static readonly DateTimeZone NewYork = DateTimeZoneProviders.Tzdb["America/New_York"];
    private static readonly LocalTime Open = new(9, 30);
    private static readonly LocalTime Close = new(16, 0);
    private static readonly LocalTime HalfDayClose = new(13, 0);

    /// <summary>Weekday rows (trading days and holidays) in [from, to].</summary>
    public IReadOnlyList<ProviderSession> Days(string mic, DateOnly from, DateOnly to)
    {
        var days = new List<ProviderSession>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (Session(mic, date) is { } session)
            {
                days.Add(session);
            }
        }

        return days;
    }

    /// <summary>The weekday row for <paramref name="date"/>, or null at weekends.</summary>
    public ProviderSession? Session(string mic, DateOnly date)
    {
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return null;
        }

        if (fixtures.Calendar.TryGetValue(date, out var special) && !special.IsHalfDay)
        {
            return new ProviderSession(mic, date, null, null, IsHalfDay: false, IsHoliday: true);
        }

        var halfDay = special?.IsHalfDay ?? false;
        return new ProviderSession(mic, date, At(date, Open), At(date, halfDay ? HalfDayClose : Close), halfDay, IsHoliday: false);
    }

    /// <summary>The latest trading session that has opened at <paramref name="now"/>.</summary>
    public ProviderSession CurrentSession(string mic, DateTimeOffset now)
    {
        var date = DateOnly.FromDateTime(Instant.FromDateTimeOffset(now).InZone(NewYork).Date.ToDateTimeUnspecified());
        while (true)
        {
            if (Session(mic, date) is { IsHoliday: false } session && session.OpenAt <= now)
            {
                return session;
            }

            date = date.AddDays(-1);
        }
    }

    /// <summary>The <paramref name="count"/> trading sessions before <paramref name="session"/>, oldest first.</summary>
    public IReadOnlyList<ProviderSession> SessionsBefore(string mic, ProviderSession session, int count)
    {
        var sessions = new List<ProviderSession>(count);
        var date = session.SessionDate.AddDays(-1);
        while (sessions.Count < count)
        {
            if (Session(mic, date) is { IsHoliday: false } previous)
            {
                sessions.Add(previous);
            }

            date = date.AddDays(-1);
        }

        sessions.Reverse();
        return sessions;
    }

    private static DateTimeOffset At(DateOnly date, LocalTime time) =>
        new LocalDate(date.Year, date.Month, date.Day).At(time).InZoneStrictly(NewYork).ToDateTimeOffset().ToUniversalTime();
}
