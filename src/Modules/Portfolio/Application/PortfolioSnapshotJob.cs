using Astryx.Contracts.MarketData;
using Astryx.Infrastructure.Jobs;
using Astryx.Modules.Portfolio.Domain;
using Astryx.Modules.Portfolio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Astryx.Modules.Portfolio.Application;

/// <summary>
/// <c>PortfolioSnapshotJob</c> (SYSTEM_DESIGN.md §19.2, §20.11): every 15 minutes. In session it writes the hour's
/// snapshot once (slot = the top of the hour); after the close it writes the session's EOD snapshot once (slot = the
/// close). The primary key makes both idempotent. The benchmark close is SPY's latest quote.
/// </summary>
public sealed record PortfolioSnapshotJob : IJob
{
    public static string JobType => "portfolio.snapshot.v1";
}

public sealed class PortfolioSnapshotHandler(PortfolioDbContext db, ValuationService valuation, IMarketSessionClock sessions, IInstrumentDirectory instruments, ILogger<PortfolioSnapshotHandler> logger)
    : IJobHandler<PortfolioSnapshotJob>
{
    public async Task HandleAsync(PortfolioSnapshotJob job, JobContext context, CancellationToken cancellationToken)
    {
        if (await sessions.TodayAsync(cancellationToken) is not { } session || sessions.Now < session.OpenAt)
        {
            return;
        }

        var now = sessions.Now;
        var (kind, slot) = now < session.CloseAt
            ? (SnapshotKind.Hourly, new DateTimeOffset(now.UtcTicks - (now.UtcTicks % TimeSpan.TicksPerHour), TimeSpan.Zero))
            : (SnapshotKind.Eod, session.CloseAt);
        var benchmark = (await instruments.FindAsync(ValuationService.Benchmark, cancellationToken))?.Last;
        var portfolios = await db.Portfolios.IgnoreQueryFilters().AsNoTracking()
            .Where(p => !db.Snapshots.Any(s => s.PortfolioId == p.Id && s.Kind == kind && s.SnapshotAt == slot)).ToListAsync(cancellationToken);
        foreach (var portfolio in portfolios)
        {
            var value = (await valuation.ValueAsync(portfolio, cancellationToken)).Portfolio;
            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO portfolio.snapshots (portfolio_id, kind, snapshot_at, equity, cash, invested, unrealized_pnl, realized_pnl_cum, benchmark_close, positions_count, stale)
                VALUES ({portfolio.Id}, {kind}, {slot}, {value.Equity}, {value.Cash}, {value.Invested}, {value.UnrealizedPnl}, {portfolio.RealizedPnl}, {benchmark}, {value.OpenPositions}, {value.Stale})
                ON CONFLICT DO NOTHING
                """,
                cancellationToken);
        }

        if (portfolios.Count > 0)
        {
            logger.LogInformation("Wrote {Count} {Kind} portfolio snapshot(s) at {Slot}", portfolios.Count, kind, slot);
        }
    }
}
