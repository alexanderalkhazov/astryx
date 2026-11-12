using Astryx.Infrastructure.Jobs;
using Astryx.Modules.MarketData.Application.Providers;
using Astryx.Modules.MarketData.Domain;
using Astryx.Modules.MarketData.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.MarketData.Application.Ingestion;

public sealed record CalendarSyncJob : IJob
{
    public static string JobType => "market.calendar_sync.v1";
}

/// <summary>Weekly: stores the session calendar for every exchange in use, a month back to a year ahead (SYSTEM_DESIGN.md §16.3).</summary>
public sealed class CalendarSyncHandler(MarketDataDbContext db, IMarketDataProvider provider, SyncStateStore state, SyncRunner runner, MarketClock clock)
    : IJobHandler<CalendarSyncJob>
{
    public async Task HandleAsync(CalendarSyncJob job, JobContext context, CancellationToken cancellationToken)
    {
        var last = await state.GetAsync(provider.Code, SyncDataset.Calendar, cancellationToken);
        if (last?.LastSuccessAt >= clock.Now.AddDays(-7))
        {
            return;
        }

        await runner.RunAsync(SyncDataset.Calendar, meter => SyncAsync(meter, cancellationToken), rethrowUnavailable: false, cancellationToken);
    }

    private async Task SyncAsync(RequestMeter meter, CancellationToken cancellationToken)
    {
        var mics = await db.Instruments.Where(i => i.IsActive).Select(i => i.ExchangeMic).Distinct().ToListAsync(cancellationToken);
        mics = mics.Append(MarketClock.PrimaryMic).Distinct().ToList();
        var from = clock.Today.AddDays(-30);
        var to = clock.Today.AddDays(365);
        foreach (var mic in mics)
        {
            meter.Take();
            var sessions = await provider.GetCalendarAsync(mic, from, to, cancellationToken);
            var existing = await db.TradingCalendar.Where(s => s.ExchangeMic == mic && s.SessionDate >= from && s.SessionDate <= to).ToDictionaryAsync(s => s.SessionDate, cancellationToken);
            foreach (var session in sessions)
            {
                if (!existing.TryGetValue(session.SessionDate, out var row))
                {
                    row = new TradingSession { ExchangeMic = mic, SessionDate = session.SessionDate };
                    db.TradingCalendar.Add(row);
                }

                row.OpenAt = session.OpenAt;
                row.CloseAt = session.CloseAt;
                row.IsHalfDay = session.IsHalfDay;
                row.IsHoliday = session.IsHoliday;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
