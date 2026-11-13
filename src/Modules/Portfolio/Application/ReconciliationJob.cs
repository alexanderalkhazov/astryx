using System.Globalization;
using System.Text.Json;
using Astryx.Contracts.Audit;
using Astryx.Contracts.MarketData;
using Astryx.Contracts.Paper;
using Astryx.Infrastructure.Jobs;
using Astryx.Modules.Portfolio.Domain;
using Astryx.Modules.Portfolio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PortfolioEntity = Astryx.Modules.Portfolio.Domain.Portfolio;

namespace Astryx.Modules.Portfolio.Application;

/// <summary>
/// <c>ReconciliationJob</c> (SYSTEM_DESIGN.md §20.13): outside the regular session, each portfolio not checked in the
/// last 20 hours is reconciled once. Any mismatch is a critical log, an audit entry and <c>reconciliation_halted_at</c>
/// (new entries blocked, exits allowed). A halt is never cleared automatically.
/// </summary>
public sealed record ReconciliationJob : IJob
{
    public static string JobType => "portfolio.reconciliation.v1";
}

public sealed class ReconciliationHandler(PortfolioReconciler reconciler, PortfolioDbContext db, IMarketSessionClock sessions) : IJobHandler<ReconciliationJob>
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(20);

    public async Task HandleAsync(ReconciliationJob job, JobContext context, CancellationToken cancellationToken)
    {
        var now = sessions.Now;
        if (await sessions.TodayAsync(cancellationToken) is { } session && now >= session.OpenAt && now < session.CloseAt)
        {
            return; // nightly: never while orders are filling
        }

        var due = now - Interval;
        var ids = await db.Portfolios.IgnoreQueryFilters().AsNoTracking().Where(p => p.ReconciledAt == null || p.ReconciledAt < due).Select(p => p.Id).ToListAsync(cancellationToken);
        foreach (var id in ids)
        {
            await reconciler.ReconcileAsync(id, cancellationToken);
        }
    }
}

/// <summary>The six checks for one portfolio. Returns the mismatches (empty when clean) and records the outcome.</summary>
public sealed class PortfolioReconciler(PortfolioDbContext db, IPaperReconciliationFacts paper, IMarketSessionClock sessions, ILogger<PortfolioReconciler> logger)
{
    /// <summary>The expiry job runs every minute; anything still working this long after its expiry is a miss.</summary>
    public static readonly TimeSpan ExpiryGrace = TimeSpan.FromMinutes(5);

    public const decimal Tolerance = 0.01m;

    public async Task<IReadOnlyList<string>> ReconcileAsync(Guid portfolioId, CancellationToken cancellationToken)
    {
        var now = sessions.Now;
        var portfolio = await db.Portfolios.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == portfolioId, cancellationToken);
        var issues = new List<string>();

        // 1. Ledger sum = cash cache (and every balance_after is a running sum).
        issues.AddRange((await LedgerInvariants.CheckAsync(db, portfolioId, cancellationToken)).Select(v => $"ledger: {v}"));

        // 2–3. Position quantities and realized P&L against fills.
        var positions = await db.Positions.IgnoreQueryFilters().AsNoTracking().Where(p => p.PortfolioId == portfolioId).ToListAsync(cancellationToken);
        var fills = (await paper.FillTotalsAsync(portfolioId, cancellationToken)).ToDictionary(f => f.PositionId);
        foreach (var p in positions)
        {
            if (!fills.TryGetValue(p.Id, out var f))
            {
                issues.Add($"position {p.Symbol} {p.Id}: no fills");
                continue;
            }

            var reset = p.ExitReason == ExitReason.Reset;
            var expectedSold = p.Status == PositionStatus.Open || reset ? 0m : p.Quantity;
            if (f.BoughtQuantity != p.Quantity || f.SoldQuantity != expectedSold)
            {
                issues.Add($"position {p.Symbol} {p.Id}: quantity {Q(p.Quantity)} but fills bought {Q(f.BoughtQuantity)} and sold {Q(f.SoldQuantity)}");
            }

            if (p.Status == PositionStatus.Closed)
            {
                // A reset closes at the last price without a fill (P11).
                var proceeds = reset ? p.Quantity * (p.ExitPrice ?? 0m) : f.SoldValue;
                var expected = proceeds - f.BoughtValue - f.Fees;
                if (p.RealizedPnl is not { } realized || Math.Abs(realized - expected) > Tolerance)
                {
                    issues.Add($"position {p.Symbol} {p.Id}: realized {M(p.RealizedPnl ?? 0m)} but fills give {M(expected)}");
                }
            }
        }

        var epochRealized = positions.Where(p => p.Status == PositionStatus.Closed && p.ClosedAt > portfolio.EpochStartedAt).Sum(p => p.RealizedPnl ?? 0m);
        if (Math.Abs(portfolio.RealizedPnl - epochRealized) > Tolerance)
        {
            issues.Add($"portfolio realized_pnl {M(portfolio.RealizedPnl)} but its positions this epoch sum to {M(epochRealized)}");
        }

        // 4. No working orders past expiry.
        issues.AddRange((await paper.WorkingPastExpiryAsync(portfolioId, now - ExpiryGrace, cancellationToken)).Select(id => $"order {id}: still working after its expiry"));

        // 5. Every open position has both OCO exits working.
        var exits = await paper.WorkingExitsAsync(portfolioId, cancellationToken);
        foreach (var p in positions.Where(p => p.Status == PositionStatus.Open))
        {
            var legs = exits.GetValueOrDefault(p.Id);
            if (legs is null || !legs.Contains("exit_stop") || !legs.Contains("exit_target"))
            {
                issues.Add($"position {p.Symbol} {p.Id}: missing a working {(legs?.Contains("exit_stop") == true ? "target" : "stop")} exit");
            }
        }

        // 6. The latest snapshot of this epoch recomputes: equity = cash + invested.
        if (await db.Snapshots.IgnoreQueryFilters().AsNoTracking().Where(s => s.PortfolioId == portfolioId && s.SnapshotAt >= portfolio.EpochStartedAt)
                .OrderByDescending(s => s.SnapshotAt).FirstOrDefaultAsync(cancellationToken) is { } snapshot
            && Math.Abs(snapshot.Equity - (snapshot.Cash + snapshot.Invested)) > Tolerance)
        {
            issues.Add($"snapshot {snapshot.Kind} {snapshot.SnapshotAt:O}: equity {M(snapshot.Equity)} but cash + invested = {M(snapshot.Cash + snapshot.Invested)}");
        }

        await RecordAsync(portfolio, issues, now, cancellationToken);
        return issues;
    }

    private async Task RecordAsync(PortfolioEntity portfolio, List<string> issues, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (issues.Count == 0)
        {
            await db.Portfolios.IgnoreQueryFilters().Where(p => p.Id == portfolio.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.ReconciledAt, now), cancellationToken);
            return;
        }

        logger.LogCritical("Reconciliation mismatch in portfolio {PortfolioId}: {Count} issue(s), new entries halted. {Issues}", portfolio.Id, issues.Count, string.Join(" | ", issues));
        var json = JsonSerializer.Serialize(issues);
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        await db.Portfolios.IgnoreQueryFilters().Where(p => p.Id == portfolio.Id).ExecuteUpdateAsync(
            s => s.SetProperty(p => p.ReconciledAt, now)
                .SetProperty(p => p.ReconciliationHaltedAt, p => p.ReconciliationHaltedAt ?? now)
                .SetProperty(p => p.ReconciliationIssues, json),
            cancellationToken);
        db.Audit(new AuditEntry("portfolio.reconciliation_failed", AuditActor.System, portfolio.UserId, "portfolio", portfolio.Id.ToString(), Metadata: new { issues }));
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static string Q(decimal value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string M(decimal value) => Math.Round(value, 2, MidpointRounding.ToEven).ToString("0.00", CultureInfo.InvariantCulture);
}
