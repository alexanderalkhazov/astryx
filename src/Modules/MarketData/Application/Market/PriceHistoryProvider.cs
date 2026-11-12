using Astryx.Contracts.MarketData;
using Astryx.Modules.MarketData.Application.Ingestion;
using Astryx.Modules.MarketData.Domain;
using Astryx.Modules.MarketData.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.MarketData.Application.Market;

/// <summary>Daily history for the quant engine from stored bars (SYSTEM_DESIGN.md §13.2), never gap-filled.</summary>
public sealed class PriceHistoryProvider(MarketDataDbContext db, MarketQueries queries, FreshnessPolicy freshness, MarketClock clock) : IPriceHistoryProvider
{
    public async Task<DailyHistory?> GetDailyAsync(string symbol, int maxBars, CancellationToken cancellationToken)
    {
        if (await queries.FindAsync(symbol, cancellationToken) is not { } instrument)
        {
            return null;
        }

        // Enough calendar days for maxBars sessions with weekends and holidays to spare.
        var to = clock.Today;
        var from = to.AddDays(-(maxBars * 3 / 2 + 30));
        var bars = (await queries.DailyBarsAsync(instrument, from, to, cancellationToken)).TakeLast(maxBars).ToList();
        if (bars.Count == 0)
        {
            return new DailyHistory(instrument.Id, instrument.Symbol, [], null, Stale: true, []);
        }

        var quote = await db.Quotes.AsNoTracking().Where(q => q.InstrumentId == instrument.Id).Select(q => (DateTimeOffset?)q.AsOf).SingleOrDefaultAsync(cancellationToken);
        var last = bars[^1];
        var forming = last.Final is false;
        var lastFinal = bars.LastOrDefault(b => b.Final is null)?.D;
        var stale = (await freshness.QuoteAsync(quote, cancellationToken)).Stale || await freshness.DailyBarsStaleAsync(lastFinal, cancellationToken);

        var first = bars[0].D;
        var stored = bars.Select(b => b.D).ToHashSet();
        var missing = await db.TradingCalendar.AsNoTracking()
            .Where(s => s.ExchangeMic == instrument.ExchangeMic && !s.IsHoliday && s.SessionDate >= first && s.SessionDate <= last.D)
            .Select(s => s.SessionDate)
            .ToListAsync(cancellationToken);

        return new DailyHistory(
            instrument.Id,
            instrument.Symbol,
            bars.Select(b => new DailyBar(b.D, b.O, b.H, b.L, b.C, b.V, Final: b.Final is null)).ToList(),
            forming ? quote : last.T,
            stale,
            missing.Where(d => !stored.Contains(d)).Order().ToList());
    }

    public async Task<CloseLadder> GetCloseLadderAsync(IReadOnlyList<int> sessionsBack, CancellationToken cancellationToken)
    {
        var offsets = sessionsBack.Distinct().ToArray();
        var rows = await db.Database
            .SqlQuery<LadderRow>(
                $"""
                SELECT i.symbol, r.n, r.close, r.session_date
                FROM (
                    SELECT instrument_id, close, session_date,
                           row_number() OVER (PARTITION BY instrument_id ORDER BY bar_start DESC) - 1 AS n
                    FROM market.price_bars WHERE timeframe = {Timeframe.OneDay} AND is_final
                ) r
                JOIN market.instruments i ON i.id = r.instrument_id AND i.is_active
                WHERE r.n = ANY({offsets})
                """)
            .ToListAsync(cancellationToken);

        var index = sessionsBack.Select((n, i) => (n, i)).ToLookup(x => x.n, x => x.i);
        var closes = new Dictionary<string, decimal?[]>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!closes.TryGetValue(row.Symbol, out var ladder))
            {
                closes[row.Symbol] = ladder = new decimal?[sessionsBack.Count];
            }

            foreach (var i in index[(int)row.N])
            {
                ladder[i] = row.Close;
            }
        }

        var through = rows.Where(r => r.N == 0).Select(r => (DateOnly?)r.SessionDate).Max();
        return new CloseLadder(through, closes);
    }

    private sealed record LadderRow(string Symbol, long N, decimal Close, DateOnly SessionDate);
}
