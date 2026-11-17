using System.Globalization;
using System.Text;
using Astryx.Contracts.MarketData;
using Astryx.Contracts.Profiles;
using Astryx.Infrastructure.Security;
using Astryx.Modules.Portfolio.Domain;
using Astryx.Modules.Portfolio.Infrastructure.Persistence;
using Astryx.SharedKernel.Finance;
using Astryx.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using PortfolioEntity = Astryx.Modules.Portfolio.Domain.Portfolio;

namespace Astryx.Modules.Portfolio.Application;

/// <summary>P1 (SYSTEM_DESIGN.md §11.8). Valuation only: unrealized P&amp;L is never posted.</summary>
public sealed record PortfolioDto(
    Guid Id, string Mode, string Currency, decimal Equity, decimal Cash, decimal CashAvailable, decimal Invested, decimal UnrealizedPnl, decimal DayPnl, decimal? DayPnlPct,
    decimal AllTimePnl, decimal AllTimeReturnPct, decimal RealizedPnl, int OpenPositions, bool Halted, DateTimeOffset AsOf, bool Stale, string? HaltReason = null);

/// <summary>P2 row. Open rows carry valuation and weight against the cap; closed rows carry the exit.</summary>
public sealed record PositionDto(
    Guid Id, string Symbol, string Name, string Status, decimal Quantity, decimal AvgEntryPrice, decimal? Last, decimal? MarketValue, decimal? UnrealizedPnl, decimal? UnrealizedPct,
    decimal? WeightPct, decimal WeightCapPct, bool OverCap, decimal? StopPrice, decimal? TargetPrice, decimal? StopDistancePct, bool NearStop, bool IsNew,
    DateTimeOffset OpenedAt, decimal? ExitPrice, string? ExitReason, decimal? RealizedPnl, DateTimeOffset? ClosedAt);

public sealed record PositionPageDto(IReadOnlyList<PositionDto> Items, string? NextCursor);

public sealed record PerformancePointDto(DateTimeOffset T, decimal Equity, decimal? Benchmark);

public sealed record PerformanceMetricsDto(decimal? TwrPct, decimal? BenchmarkPct, decimal MaxDrawdownPct, decimal? WinRate, decimal? AvgWin, decimal? AvgLoss, int Trades);

/// <summary>P5: equity and the benchmark (rebased to the first equity value) with metrics for the range.</summary>
public sealed record PerformanceDto(string Range, string Benchmark, IReadOnlyList<PerformancePointDto> Series, PerformanceMetricsDto Metrics);

/// <summary>P6 row: a closed position.</summary>
public sealed record ClosedTradeDto(Guid PositionId, string Symbol, decimal Quantity, decimal EntryPrice, decimal ExitPrice, string ExitReason, decimal RealizedPnl, decimal ReturnPct, DateTimeOffset OpenedAt, DateTimeOffset ClosedAt, int HoldingDays);

public sealed record ClosedTradePageDto(IReadOnlyList<ClosedTradeDto> Items, string? NextCursor);

/// <summary>A valued portfolio: the DTO plus per-position marks, shared by P1, P2 and the snapshot job.</summary>
public sealed record Mark(decimal? Last, DateTimeOffset? AsOf, string Name, string? Sector);

public sealed record Valuation(PortfolioDto Portfolio, IReadOnlyDictionary<Guid, Mark> Marks);

/// <summary>
/// <c>ValuationService</c> (§7.12, §15.6): equity = cash + Σ qty × last. Stale when any open position's quote is
/// missing or the feed is stale. Day P&amp;L is against start-of-day equity; all-time against starting cash.
/// </summary>
public sealed class ValuationService(PortfolioDbContext db, ICurrentUser user, IInstrumentDirectory instruments, IMarketDataFreshness freshness, IRiskSettingsSource riskSettings, IMarketSessionClock sessions)
{
    public const string Benchmark = "SPY";
    public static readonly Error NotFound = new("not_found", "No paper portfolio yet.");
    public static readonly Error BadRange = new("bad_request", "range must be 1D, 1W, 1M, 3M, 1Y or ALL.");
    public static readonly Error BadStatus = new("bad_request", "status must be open or closed.");

    public async Task<Result<PortfolioDto>> GetAsync(CancellationToken cancellationToken) =>
        await CurrentAsync(cancellationToken) is { } portfolio ? (await ValueAsync(portfolio, cancellationToken)).Portfolio : NotFound;

    public async Task<Valuation> ValueAsync(PortfolioEntity portfolio, CancellationToken cancellationToken)
    {
        var open = await db.Positions.IgnoreQueryFilters().AsNoTracking().Where(p => p.PortfolioId == portfolio.Id && p.Status == PositionStatus.Open).ToListAsync(cancellationToken);
        var summaries = (await instruments.GetAsync(open.Select(p => p.InstrumentId).Distinct().ToList(), cancellationToken)).ToDictionary(s => s.Id);
        var marks = open.Select(p => p.InstrumentId).Distinct().ToDictionary(
            id => id, id => summaries.GetValueOrDefault(id) is { } s ? new Mark(s.Last, s.AsOf, s.Name, s.Sector) : new Mark(null, null, "", null));
        var invested = open.Sum(p => p.Quantity * (marks[p.InstrumentId].Last ?? p.AvgEntryPrice));
        var unrealized = open.Sum(p => p.Quantity * ((marks[p.InstrumentId].Last ?? p.AvgEntryPrice) - p.AvgEntryPrice));
        var equity = portfolio.CashBalance + invested;
        var feed = await freshness.GetAsync(cancellationToken);
        var stale = open.Count > 0 && (feed.QuotesStale || open.Any(p => marks[p.InstrumentId].Last is null));
        var asOf = open.Select(p => marks[p.InstrumentId].AsOf).Where(a => a is not null).Max() ?? feed.QuotesAsOf ?? sessions.Now;
        decimal? dayPct = portfolio.StartOfDayEquity is > 0 and { } sod ? (equity - sod) / sod : null;
        var limits = await riskSettings.GetAsync(portfolio.UserId, cancellationToken);
        // Reconciliation wins: it lasts until an operator clears it, the daily halt only until the next session.
        var halted = portfolio.ReconciliationHaltedAt is not null ? "reconciliation" : dayPct is { } halt && halt <= -limits.DailyLossHaltPct ? "daily_loss" : null;
        var dto = new PortfolioDto(
            portfolio.Id, "paper", portfolio.BaseCurrency, Money(equity), Money(portfolio.CashBalance), Money(portfolio.CashBalance - portfolio.ReservedCash), Money(invested),
            Money(unrealized), Money(portfolio.StartOfDayEquity is { } s0 ? equity - s0 : 0m), dayPct is { } d ? Pct(d) : null,
            Money(equity - portfolio.StartingCash), portfolio.StartingCash > 0 ? Pct((equity - portfolio.StartingCash) / portfolio.StartingCash) : 0m, Money(portfolio.RealizedPnl),
            open.Count, halted is not null, asOf, stale, halted);
        return new Valuation(dto, marks);
    }

    public async Task<Result<PositionPageDto>> PositionsAsync(string? status, int? limit, string? cursor, CancellationToken cancellationToken)
    {
        if (status is not (null or "open" or "closed"))
        {
            return BadStatus;
        }

        if (await CurrentAsync(cancellationToken) is not { } portfolio)
        {
            return NotFound;
        }

        var valuation = await ValueAsync(portfolio, cancellationToken);
        var limits = await riskSettings.GetAsync(portfolio.UserId, cancellationToken);
        var take = Math.Clamp(limit ?? 50, 1, 100);
        var wantOpen = status is null or "open";
        var query = db.Positions.IgnoreQueryFilters().AsNoTracking().Where(p => p.PortfolioId == portfolio.Id && p.Status == (wantOpen ? PositionStatus.Open : PositionStatus.Closed));
        if (cursor is not null && Decode(cursor) is { } after)
        {
            query = wantOpen
                ? query.Where(p => p.OpenedAt < after.At || (p.OpenedAt == after.At && p.Id.CompareTo(after.Id) < 0))
                : query.Where(p => p.ClosedAt < after.At || (p.ClosedAt == after.At && p.Id.CompareTo(after.Id) < 0));
        }

        var rows = wantOpen
            ? await query.OrderByDescending(p => p.OpenedAt).ThenByDescending(p => p.Id).Take(take + 1).ToListAsync(cancellationToken)
            : await query.OrderByDescending(p => p.ClosedAt).ThenByDescending(p => p.Id).Take(take + 1).ToListAsync(cancellationToken);
        var names = (await instruments.GetAsync(rows.Select(r => r.InstrumentId).Distinct().ToList(), cancellationToken)).ToDictionary(i => i.Id, i => i.Name);
        var equity = valuation.Portfolio.Equity;
        var items = rows.Take(take).Select(p =>
        {
            var name = names.GetValueOrDefault(p.InstrumentId, p.Symbol);
            if (p.Status == PositionStatus.Closed)
            {
                return new PositionDto(p.Id, p.Symbol, name, p.Status, Whole(p.Quantity), Avg(p.AvgEntryPrice), null, null, null, null, null, Pct(limits.MaxPositionWeightPct), false,
                    Px(p.StopPrice), Px(p.TargetPrice), null, false, false, p.OpenedAt, Px(p.ExitPrice), p.ExitReason, p.RealizedPnl is { } r ? Money(r) : null, p.ClosedAt);
            }

            var last = valuation.Marks[p.InstrumentId].Last;
            var value = last is { } l ? p.Quantity * l : (decimal?)null;
            decimal? weight = value is { } v && equity > 0 ? v / equity : null;
            decimal? stopDistance = last is { } x && p.StopPrice is { } stop && x > 0 ? (x - stop) / x : null;
            return new PositionDto(p.Id, p.Symbol, name, p.Status, Whole(p.Quantity), Avg(p.AvgEntryPrice), Px(last), value is { } mv ? Money(mv) : null,
                last is { } lp ? Money(p.Quantity * (lp - p.AvgEntryPrice)) : null, last is { } lq ? Pct((lq / p.AvgEntryPrice) - 1) : null,
                weight is { } w ? Pct(w) : null, Pct(limits.MaxPositionWeightPct), weight > limits.MaxPositionWeightPct, Px(p.StopPrice), Px(p.TargetPrice),
                stopDistance is { } sd ? Pct(sd) : null, stopDistance is > 0 and <= 0.015m, p.SeenAt is null, p.OpenedAt, null, null, null, null);
        }).ToList();
        var next = rows.Count > take ? Encode(wantOpen ? rows[take - 1].OpenedAt : rows[take - 1].ClosedAt!.Value, rows[take - 1].Id) : null;
        return new PositionPageDto(items, next);
    }

    public async Task<Result<PerformanceDto>> PerformanceAsync(string? range, CancellationToken cancellationToken)
    {
        var r = (range ?? "1M").ToUpperInvariant();
        var now = sessions.Now;
        DateTimeOffset? from = r switch
        {
            "1D" => now.AddDays(-1),
            "1W" => now.AddDays(-7),
            "1M" => now.AddMonths(-1),
            "3M" => now.AddMonths(-3),
            "1Y" => now.AddYears(-1),
            "ALL" => null,
            _ => DateTimeOffset.MaxValue,
        };
        if (from == DateTimeOffset.MaxValue)
        {
            return BadRange;
        }

        if (await CurrentAsync(cancellationToken) is not { } portfolio)
        {
            return NotFound;
        }

        var kind = r is "1D" or "1W" ? SnapshotKind.Hourly : SnapshotKind.Eod;
        var snapshots = await db.Snapshots.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.PortfolioId == portfolio.Id && s.Kind == kind && s.SnapshotAt >= portfolio.EpochStartedAt && (from == null || s.SnapshotAt >= from))
            .OrderBy(s => s.SnapshotAt).ToListAsync(cancellationToken);
        var current = (await ValueAsync(portfolio, cancellationToken)).Portfolio;
        var benchmarkNow = (await instruments.FindAsync(Benchmark, cancellationToken))?.Last;
        var points = snapshots.Select(s => (s.SnapshotAt, s.Equity, s.BenchmarkClose)).Append((now, current.Equity, benchmarkNow)).ToList();
        var rebased = PerformanceMath.Rebase(points.Select(p => p.Item3).ToList(), points[0].Item2);
        var series = points.Select((p, i) => new PerformancePointDto(p.Item1, Money(p.Item2), rebased[i] is { } b ? Money(b) : null)).ToList();

        var eod = await db.Snapshots.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.PortfolioId == portfolio.Id && s.Kind == SnapshotKind.Eod && s.SnapshotAt >= portfolio.EpochStartedAt && (from == null || s.SnapshotAt >= from))
            .OrderBy(s => s.SnapshotAt).Select(s => s.Equity).ToListAsync(cancellationToken);
        var twr = PerformanceMath.TimeWeightedReturn([.. eod, current.Equity]);
        var benchFirst = points.Select(p => p.Item3).FirstOrDefault(b => b is > 0);
        decimal? benchPct = benchFirst is { } bf && benchmarkNow is { } bn ? (bn / bf) - 1 : null;
        var closed = await db.Positions.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.PortfolioId == portfolio.Id && p.Status == PositionStatus.Closed && p.ClosedAt > portfolio.EpochStartedAt && (from == null || p.ClosedAt >= from) && p.RealizedPnl != null)
            .Select(p => p.RealizedPnl!.Value).ToListAsync(cancellationToken);
        var stats = PerformanceMath.Trades(closed);
        var metrics = new PerformanceMetricsDto(
            twr is { } t ? Pct(t) : null, benchPct is { } bp ? Pct(bp) : null, Pct(PerformanceMath.MaxDrawdown(points.Select(p => p.Item2).ToList())),
            stats.WinRate is { } w ? Math.Round(w, 2, MidpointRounding.ToEven) + 0.00m : null, stats.AvgWin is { } aw ? Money(aw) : null, stats.AvgLoss is { } al ? Money(al) : null, stats.Trades);
        return new PerformanceDto(r, Benchmark, series, metrics);
    }

    public async Task<Result<ClosedTradePageDto>> TradesAsync(int? limit, string? cursor, CancellationToken cancellationToken)
    {
        if (await CurrentAsync(cancellationToken) is not { } portfolio)
        {
            return NotFound;
        }

        var take = Math.Clamp(limit ?? 50, 1, 100);
        // The current epoch only: positions closed by a reset (at the epoch start) belong to the previous one.
        var query = db.Positions.IgnoreQueryFilters().AsNoTracking().Where(p => p.PortfolioId == portfolio.Id && p.Status == PositionStatus.Closed && p.ClosedAt > portfolio.EpochStartedAt);
        if (cursor is not null && Decode(cursor) is { } after)
        {
            query = query.Where(p => p.ClosedAt < after.At || (p.ClosedAt == after.At && p.Id.CompareTo(after.Id) < 0));
        }

        var rows = await query.OrderByDescending(p => p.ClosedAt).ThenByDescending(p => p.Id).Take(take + 1).ToListAsync(cancellationToken);
        var items = rows.Take(take).Select(p => new ClosedTradeDto(
            p.Id, p.Symbol, Whole(p.Quantity), Px(p.AvgEntryPrice)!.Value, Px(p.ExitPrice)!.Value, p.ExitReason!, Money(p.RealizedPnl ?? 0m),
            Pct((p.RealizedPnl ?? 0m) / (p.Quantity * p.AvgEntryPrice)), p.OpenedAt, p.ClosedAt!.Value, (p.ClosedAt.Value.Date - p.OpenedAt.Date).Days)).ToList();
        return new ClosedTradePageDto(items, rows.Count > take ? Encode(rows[take - 1].ClosedAt!.Value, rows[take - 1].Id) : null);
    }

    private Task<PortfolioEntity?> CurrentAsync(CancellationToken cancellationToken) =>
        db.Portfolios.AsNoTracking().ActiveFor(user.RequireUserId()).FirstOrDefaultAsync(cancellationToken);

    private static decimal Money(decimal v) => RoundingPolicy.Ledger(v);

    private static decimal Pct(decimal fraction) => Math.Round(fraction, 4, MidpointRounding.ToEven);

    private static decimal? Px(decimal? p) => p is { } v ? Math.Round(v, 2, MidpointRounding.ToEven) : null;

    private static decimal Avg(decimal p) => Math.Round(p, 2, MidpointRounding.ToEven);

    private static decimal Whole(decimal q) => Math.Round(q, 0, MidpointRounding.ToEven);

    private static string Encode(DateTimeOffset at, Guid id) => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{at.UtcTicks.ToString(CultureInfo.InvariantCulture)}|{id:N}"));

    private static (DateTimeOffset At, Guid Id)? Decode(string cursor)
    {
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
            return parts.Length == 2 && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) && Guid.TryParseExact(parts[1], "N", out var id)
                ? (new DateTimeOffset(ticks, TimeSpan.Zero), id) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
