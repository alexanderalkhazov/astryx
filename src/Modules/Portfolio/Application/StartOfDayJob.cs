using Astryx.Contracts.MarketData;
using Astryx.Infrastructure.Jobs;
using Astryx.Modules.Portfolio.Domain;
using Astryx.Modules.Portfolio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Astryx.Modules.Portfolio.Application;

/// <summary>
/// <c>StartOfDayJob</c> (SYSTEM_DESIGN.md §7.13, §14.4): once the regular session opens, each portfolio's equity at
/// the open (cash + open positions at their latest quotes) becomes <c>start_of_day_equity</c>, the baseline for the
/// daily loss halt. Runs every few minutes and touches only portfolios not yet stamped for today's session.
/// </summary>
public sealed record StartOfDayJob : IJob
{
    public static string JobType => "portfolio.start_of_day.v1";
}

public sealed class StartOfDayHandler(PortfolioDbContext db, IMarketSessionClock sessions, IInstrumentDirectory instruments, ILogger<StartOfDayHandler> logger)
    : IJobHandler<StartOfDayJob>
{
    public async Task HandleAsync(StartOfDayJob job, JobContext context, CancellationToken cancellationToken)
    {
        if (await sessions.TodayAsync(cancellationToken) is not { } session || sessions.Now < session.OpenAt)
        {
            return;
        }

        var due = await db.Portfolios.IgnoreQueryFilters()
            .Where(p => p.StartOfDayDate == null || p.StartOfDayDate < session.Date).Select(p => new { p.Id, p.CashBalance }).ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            return;
        }

        var ids = due.Select(d => d.Id).ToList();
        var open = await db.Positions.IgnoreQueryFilters().Where(p => ids.Contains(p.PortfolioId) && p.Status == PositionStatus.Open)
            .Select(p => new { p.PortfolioId, p.InstrumentId, p.Quantity }).ToListAsync(cancellationToken);
        var last = (await instruments.GetAsync(open.Select(p => p.InstrumentId).Distinct().ToList(), cancellationToken)).ToDictionary(i => i.Id, i => i.Last ?? 0m);
        foreach (var portfolio in due)
        {
            var equity = portfolio.CashBalance + open.Where(p => p.PortfolioId == portfolio.Id).Sum(p => p.Quantity * last.GetValueOrDefault(p.InstrumentId));
            await db.Portfolios.IgnoreQueryFilters()
                .Where(p => p.Id == portfolio.Id && (p.StartOfDayDate == null || p.StartOfDayDate < session.Date))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.StartOfDayEquity, equity).SetProperty(p => p.StartOfDayDate, session.Date), cancellationToken);
        }

        logger.LogInformation("Stamped start-of-day equity for {Count} portfolio(s) for {Session}", due.Count, session.Date);
    }
}
