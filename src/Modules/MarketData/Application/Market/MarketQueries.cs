using System.Text.Json.Serialization;
using Astryx.Contracts.MarketData;
using Astryx.Modules.MarketData.Application.Ingestion;
using Astryx.Modules.MarketData.Application.Providers;
using Astryx.Modules.MarketData.Domain;
using Astryx.Modules.MarketData.Infrastructure.Persistence;
using Astryx.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.MarketData.Application.Market;

public sealed record InstrumentDto(string Symbol, string Exchange, string Name, string AssetType, string Currency, string? Sector, string? Industry, decimal? MarketCap, bool IsActive);

public sealed record QuoteDto(
    string Symbol,
    decimal Last,
    decimal Change,
    decimal ChangePct,
    decimal Open,
    decimal High,
    decimal Low,
    decimal PrevClose,
    decimal? Bid,
    decimal? Ask,
    long Volume,
    DateTimeOffset AsOf,
    string Quality,
    bool Stale,
    string Source);

/// <summary>One bar; short names keep 1,000-bar payloads small (§11.4 M6). <c>Final</c> is omitted (null) when true.</summary>
public sealed record BarDto(DateTimeOffset T, DateOnly D, decimal O, decimal H, decimal L, decimal C, long V, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Final);

public sealed record LevelsDto(decimal Entry, decimal Stop, decimal Target);

public sealed record BarsDto(string Symbol, string Interval, bool Adjusted, DateTimeOffset? AsOf, bool Stale, IReadOnlyList<BarDto> Bars, LevelsDto? Levels);

public sealed record FundamentalsDto(
    string Symbol,
    DateTimeOffset? AsOf,
    decimal? Beta,
    decimal? PeRatio,
    decimal? EpsTtm,
    DateOnly? NextEarningsDate,
    decimal? MarketCap,
    string? Sector,
    string? Industry);

public sealed record NewsItemDto(Guid Id, string Headline, string Source, string Url, DateTimeOffset PublishedAt, string? Summary);

public sealed record BreadthDto(int Advancers, int Decliners, decimal PctAbove50dma, string Sentiment, DateTimeOffset AsOf, bool Stale);

/// <summary>A chart request: either a display range (<c>3M</c>) or an explicit interval with dates.</summary>
public sealed record BarsQuery(string? Timeframe, string? Interval, DateOnly? From, DateOnly? To, int? Limit);

/// <summary>
/// Read side of the market endpoints (SYSTEM_DESIGN.md §11.4). Answers from what ingestion stored, with freshness from
/// <see cref="FreshnessPolicy"/>; the provider is called only for data that is not stored (intraday bars, a missing
/// quote, unsynced news), which is where a provider outage surfaces as <see cref="MarketDataUnavailableException"/>.
/// </summary>
public sealed class MarketQueries(
    MarketDataDbContext db,
    IMarketDataProvider provider,
    NewsIngestor news,
    FreshnessPolicy freshness,
    MarketClock clock,
    IEnumerable<IChartLevelsSource> levelSources)
{
    public static readonly Error InstrumentNotFound = new("instrument_not_found", "No instrument with that symbol.");
    public const int MaxBars = 1000;

    private static readonly Dictionary<string, (BarInterval Interval, Func<DateOnly, DateOnly> From)> Ranges = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1D"] = (BarInterval.FiveMinutes, today => today),
        ["5D"] = (BarInterval.FifteenMinutes, today => today.AddDays(-7)),
        ["1M"] = (BarInterval.OneDay, today => today.AddMonths(-1)),
        ["3M"] = (BarInterval.OneDay, today => today.AddMonths(-3)),
        ["6M"] = (BarInterval.OneDay, today => today.AddMonths(-6)),
        ["1Y"] = (BarInterval.OneDay, today => today.AddYears(-1)),
        ["ALL"] = (BarInterval.OneDay, _ => DateOnly.MinValue),
    };

    private static readonly Dictionary<string, BarInterval> Intervals = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1m"] = BarInterval.OneMinute, ["5m"] = BarInterval.FiveMinutes, ["15m"] = BarInterval.FifteenMinutes, ["1h"] = BarInterval.OneHour, ["1d"] = BarInterval.OneDay,
    };

    public async Task<Result<InstrumentDto>> InstrumentAsync(string symbol, CancellationToken cancellationToken) =>
        await FindAsync(symbol, cancellationToken) is { } i
            ? new InstrumentDto(i.Symbol, i.ExchangeMic, i.Name, i.AssetType, i.Currency, i.Sector, i.Industry, i.MarketCap, i.IsActive)
            : InstrumentNotFound;

    public async Task<Result<QuoteDto>> QuoteAsync(string symbol, CancellationToken cancellationToken)
    {
        if (await FindAsync(symbol, cancellationToken) is not { } instrument)
        {
            return InstrumentNotFound;
        }

        var stored = await db.Quotes.AsNoTracking().SingleOrDefaultAsync(q => q.InstrumentId == instrument.Id, cancellationToken);
        if (stored is null)
        {
            // Not synced yet: ask the provider directly (an outage becomes 503).
            var live = (await provider.GetQuotesAsync([instrument.Symbol], cancellationToken)).SingleOrDefault()
                ?? throw new MarketDataUnavailableException(provider.Code, $"no quote for {instrument.Symbol}");
            stored = new Quote
            {
                InstrumentId = instrument.Id, Last = live.Last, Bid = live.Bid, Ask = live.Ask, Open = live.Open, High = live.High, Low = live.Low,
                PrevClose = live.PrevClose, Volume = live.Volume, AsOf = live.AsOf, Source = provider.Code, Quality = live.Quality,
            };
        }

        var fresh = await freshness.QuoteAsync(stored.AsOf, cancellationToken);
        var change = stored.Last - stored.PrevClose;
        return new QuoteDto(
            instrument.Symbol, Price(stored.Last), Price(change), Ratio(stored.PrevClose == 0 ? 0 : change / stored.PrevClose),
            Price(stored.Open), Price(stored.High), Price(stored.Low), Price(stored.PrevClose),
            stored.Bid is { } bid ? Price(bid) : null, stored.Ask is { } ask ? Price(ask) : null,
            stored.Volume, stored.AsOf, stored.Quality, fresh.Stale, stored.Source);
    }

    public async Task<Result<BarsDto>> BarsAsync(string symbol, BarsQuery query, CancellationToken cancellationToken)
    {
        if (await FindAsync(symbol, cancellationToken) is not { } instrument)
        {
            return InstrumentNotFound;
        }

        if (Resolve(query) is not { } request)
        {
            return Error.Validation("Give timeframe (1D, 5D, 1M, 3M, 6M, 1Y, ALL) or interval (1m, 5m, 15m, 1h, 1d) with from < to; limit is at most 1000.");
        }

        var bars = request.Interval == BarInterval.OneDay
            ? await DailyBarsAsync(instrument, request.From, request.To, cancellationToken)
            : (await provider.GetBarsAsync(instrument.Symbol, request.Interval, request.From, request.To, cancellationToken))
                .Select(b => new BarDto(b.BarStart, b.SessionDate, Price(b.Open), Price(b.High), Price(b.Low), Price(b.Close), b.Volume, b.IsFinal ? null : false))
                .ToList();

        bars = bars.TakeLast(request.Limit).ToList();
        var quote = await db.Quotes.AsNoTracking().Where(q => q.InstrumentId == instrument.Id).Select(q => (DateTimeOffset?)q.AsOf).SingleOrDefaultAsync(cancellationToken);
        var lastFinal = bars.LastOrDefault(b => b.Final is null)?.D;
        var stale = request.Interval == BarInterval.OneDay
            ? (await freshness.QuoteAsync(quote, cancellationToken)).Stale || await freshness.DailyBarsStaleAsync(lastFinal, cancellationToken)
            : (await freshness.QuoteAsync(quote, cancellationToken)).Stale;

        LevelsDto? levels = null;
        foreach (var source in levelSources)
        {
            if (await source.GetLevelsAsync(instrument.Symbol, cancellationToken) is { } found)
            {
                levels = new LevelsDto(Price(found.Entry), Price(found.Stop), Price(found.Target));
                break;
            }
        }

        // Stored history is unadjusted until corporate-action adjustment lands (T-044).
        return new BarsDto(instrument.Symbol, request.Interval.ToTimeframe(), Adjusted: false, quote, stale, bars, levels);
    }

    public async Task<Result<FundamentalsDto>> FundamentalsAsync(string symbol, CancellationToken cancellationToken)
    {
        if (await FindAsync(symbol, cancellationToken) is not { } instrument)
        {
            return InstrumentNotFound;
        }

        var f = await db.Fundamentals.AsNoTracking().SingleOrDefaultAsync(x => x.InstrumentId == instrument.Id, cancellationToken);
        return new FundamentalsDto(instrument.Symbol, f?.AsOf, Trim(f?.Beta), Trim(f?.PeRatio), Trim(f?.EpsTtm), f?.NextEarningsDate, Trim(instrument.MarketCap), instrument.Sector, instrument.Industry);
    }

    public async Task<Result<IReadOnlyList<NewsItemDto>>> NewsAsync(string symbol, int limit, CancellationToken cancellationToken)
    {
        if (await FindAsync(symbol, cancellationToken) is not { } instrument)
        {
            return InstrumentNotFound;
        }

        var since = clock.Now.AddDays(-7);
        if (!await db.InstrumentNews.AnyAsync(n => n.InstrumentId == instrument.Id && db.NewsArticles.Any(a => a.Id == n.ArticleId && a.IngestedAt >= clock.Now.AddMinutes(-15)), cancellationToken))
        {
            await news.IngestAsync(instrument.Symbol, cancellationToken); // on demand when nothing recent is stored
        }

        var items = await db.NewsArticles.AsNoTracking()
            .Where(a => a.PublishedAt >= since && db.InstrumentNews.Any(n => n.ArticleId == a.Id && n.InstrumentId == instrument.Id))
            .OrderByDescending(a => a.PublishedAt)
            .Take(limit)
            .Select(a => new NewsItemDto(a.Id, a.Headline, a.SourceName, a.Url, a.PublishedAt, a.ProviderSummary))
            .ToListAsync(cancellationToken);
        return items;
    }

    /// <summary>Advancers vs decliners over the latest quotes, and the share trading above their 50-day average (M11).</summary>
    public async Task<Result<BreadthDto>> BreadthAsync(CancellationToken cancellationToken)
    {
        var quotes = await db.Quotes.AsNoTracking()
            .Where(q => db.Instruments.Any(i => i.Id == q.InstrumentId && i.IsActive))
            .Select(q => new { q.InstrumentId, q.Last, q.PrevClose, q.AsOf })
            .ToListAsync(cancellationToken);
        if (quotes.Count == 0)
        {
            throw new MarketDataUnavailableException(provider.Code, "no quotes have been synced yet");
        }

        var averages = await db.Database
            .SqlQuery<Average50>(
                $"""
                SELECT instrument_id, avg(close) AS average
                FROM (
                    SELECT instrument_id, close, row_number() OVER (PARTITION BY instrument_id ORDER BY bar_start DESC) AS n
                    FROM market.price_bars WHERE timeframe = {Timeframe.OneDay} AND is_final
                ) recent
                WHERE n <= 50
                GROUP BY instrument_id
                HAVING count(*) = 50
                """)
            .ToDictionaryAsync(a => a.InstrumentId, a => a.Average, cancellationToken);

        var advancers = quotes.Count(q => q.Last > q.PrevClose);
        var decliners = quotes.Count(q => q.Last < q.PrevClose);
        var measured = quotes.Where(q => averages.ContainsKey(q.InstrumentId)).ToList();
        var above = measured.Count == 0 ? 0m : (decimal)measured.Count(q => q.Last > averages[q.InstrumentId]) / measured.Count;
        var advanceShare = advancers + decliners == 0 ? 0.5m : (decimal)advancers / (advancers + decliners);
        var sentiment = advanceShare >= 0.6m && above >= 0.6m ? "risk_on" : advanceShare <= 0.4m && above <= 0.4m ? "risk_off" : "neutral";
        var asOf = quotes.Max(q => q.AsOf);
        return new BreadthDto(advancers, decliners, Ratio(above), sentiment, asOf, (await freshness.QuoteAsync(asOf, cancellationToken)).Stale);
    }

    /// <summary>
    /// Stored final daily bars, plus the current session's forming bar from the latest quote when the end-of-day sync
    /// has not stored that session yet.
    /// </summary>
    internal async Task<List<BarDto>> DailyBarsAsync(Instrument instrument, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var bars = await db.PriceBars.AsNoTracking()
            .Where(b => b.InstrumentId == instrument.Id && b.Timeframe == Timeframe.OneDay && b.SessionDate >= from && b.SessionDate <= to)
            .OrderBy(b => b.BarStart)
            .Select(b => new BarDto(b.BarStart, b.SessionDate, b.Open, b.High, b.Low, b.Close, b.Volume, b.IsFinal ? null : false))
            .ToListAsync(cancellationToken);
        bars = bars.Select(b => b with { O = Price(b.O), H = Price(b.H), L = Price(b.L), C = Price(b.C) }).ToList();

        var quote = await db.Quotes.AsNoTracking().SingleOrDefaultAsync(q => q.InstrumentId == instrument.Id, cancellationToken);
        var session = await clock.CurrentSessionAsync(cancellationToken);
        if (quote is not null && quote.AsOf >= session.OpenAt && session.SessionDate <= to && (bars.Count == 0 || bars[^1].D < session.SessionDate))
        {
            // Not final until the end-of-day sync stores it, even after the close.
            bars.Add(new BarDto(session.OpenAt!.Value, session.SessionDate, Price(quote.Open), Price(Math.Max(quote.High, quote.Last)), Price(Math.Min(quote.Low, quote.Last)), Price(quote.Last), quote.Volume, Final: false));
        }

        return bars;
    }

    private (BarInterval Interval, DateOnly From, DateOnly To, int Limit)? Resolve(BarsQuery query)
    {
        var limit = query.Limit ?? MaxBars;
        if (limit is < 1 or > MaxBars)
        {
            return null;
        }

        if (query.Timeframe is { } timeframe && query.Interval is null)
        {
            return Ranges.TryGetValue(timeframe, out var range) ? (range.Interval, range.From(clock.Today), clock.Today, limit) : null;
        }

        if (query.Interval is { } interval && query.Timeframe is null && Intervals.TryGetValue(interval, out var parsed))
        {
            var to = query.To ?? clock.Today;
            var from = query.From ?? to.AddMonths(parsed == BarInterval.OneDay ? -3 : 0);
            return from <= to ? (parsed, from, to, limit) : null;
        }

        return null;
    }

    internal Task<Instrument?> FindAsync(string symbol, CancellationToken cancellationToken)
    {
        var normalized = symbol.Trim().ToUpperInvariant();
        return db.Instruments.AsNoTracking().Where(i => i.Symbol == normalized).OrderByDescending(i => i.IsActive).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>US equity prices: cents from $1 up, four decimals below.</summary>
    private static decimal Price(decimal value) => Math.Round(value, Math.Abs(value) >= 1 ? 2 : 4, MidpointRounding.AwayFromZero);

    /// <summary>Drops storage scale: 1.7200 → 1.72.</summary>
    private static decimal? Trim(decimal? value) => value / 1.000000000000000000000000000000000m;

    /// <summary>Fractions such as change % (0.0342 = 3.42 %).</summary>
    private static decimal Ratio(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    private sealed record Average50(Guid InstrumentId, decimal Average);
}
