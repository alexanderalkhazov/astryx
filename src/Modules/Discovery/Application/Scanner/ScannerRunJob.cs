using Astryx.Infrastructure.Jobs;
using Astryx.Modules.Discovery.Domain;
using Astryx.Modules.Discovery.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Discovery.Application.Scanner;

/// <param name="Trigger"><c>schedule</c> (the recurring tick: runs only when market data moved on) or <c>manual</c> (always runs).</param>
public sealed record ScannerRunJob(string Trigger = "schedule") : IJob
{
    public static string JobType => "discovery.scanner_run.v1";
}

/// <summary>
/// ScannerRunJob (SYSTEM_DESIGN.md §19.2): every 15 min, a scan when quotes or daily bars moved on since the last run,
/// which means every tick in session and once each after the close and after the end-of-day sync. The first run after
/// new final bars is recorded as <c>eod</c>.
/// </summary>
public sealed class ScannerRunHandler(ScannerService scanner, DiscoveryDbContext db, Astryx.Contracts.MarketData.IMarketDataFreshness freshness) : IJobHandler<ScannerRunJob>
{
    public async Task HandleAsync(ScannerRunJob job, JobContext context, CancellationToken cancellationToken)
    {
        if (job.Trigger != "manual" && !await scanner.IsDueAsync(cancellationToken))
        {
            return;
        }

        var trigger = job.Trigger;
        if (trigger == "schedule")
        {
            var bars = (await freshness.GetAsync(cancellationToken)).DailyBarsThrough;
            var lastBars = await db.ScannerRuns.AsNoTracking().Where(r => r.Status != ScannerRunStatus.Running).OrderByDescending(r => r.StartedAt).Select(r => r.BarsThrough).FirstOrDefaultAsync(cancellationToken);
            if (bars is not null && bars > lastBars)
            {
                trigger = "eod";
            }
        }

        await scanner.RunAsync(trigger, cancellationToken);
    }
}
