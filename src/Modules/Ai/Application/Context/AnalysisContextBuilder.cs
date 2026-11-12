using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Astryx.Contracts.Discovery;
using Astryx.Contracts.MarketData;
using Astryx.Contracts.Quant;
using Astryx.SharedKernel.Results;

namespace Astryx.Modules.Ai.Application.Context;

/// <summary>A sanitised news item as it appears in the context and the <c>&lt;news&gt;</c> block.</summary>
public sealed record ContextNews(DateTimeOffset PublishedAt, string Source, string Headline, string? Snippet);

/// <summary>The <c>ctx.v1</c> document (SYSTEM_DESIGN.md §12.4), stored verbatim with the result for reproducibility.</summary>
public sealed record AnalysisContext(string Symbol, string Name, DateTimeOffset DataAsOf, decimal PriceBandMin, decimal PriceBandMax, IReadOnlyList<ContextNews> News, JsonObject Document)
{
    public string ToJson() => Document.ToJsonString();

    /// <summary>Rehydrates a stored or frozen <c>ctx.v1</c> (golden evaluation set, replays) exactly as it was built.</summary>
    public static AnalysisContext FromDocument(JsonObject document)
    {
        var band = document["constraints"]!["priceBand"]!;
        var news = document["news"]!.AsArray().Select(n => new ContextNews(
            DateTimeOffset.Parse(n!["publishedAt"]!.GetValue<string>(), CultureInfo.InvariantCulture),
            n["source"]!.GetValue<string>(),
            n["headline"]!.GetValue<string>(),
            n["snippet"]?.GetValue<string>())).ToList();
        return new AnalysisContext(
            document["symbol"]!.GetValue<string>(),
            document["name"]!.GetValue<string>(),
            DateTimeOffset.Parse(document["dataAsOf"]!.GetValue<string>(), CultureInfo.InvariantCulture),
            decimal.Parse(band["min"]!.GetValue<string>(), CultureInfo.InvariantCulture),
            decimal.Parse(band["max"]!.GetValue<string>(), CultureInfo.InvariantCulture),
            news,
            document);
    }

    /// <summary>The document without <c>news</c>, which the prompt carries separately inside the untrusted block.</summary>
    public string PromptJson()
    {
        var copy = (JsonObject)Document.DeepClone();
        copy.Remove("news");
        return copy.ToJsonString();
    }
}

/// <summary>
/// Builds a validated, bounded, deterministic <c>ctx.v1</c> from data MarketData, Quant and Discovery already
/// validated. Stale or insufficient data fails here, before any model call (§12.4).
/// </summary>
public sealed class AnalysisContextBuilder(
    IInstrumentDirectory instruments, IPriceHistoryProvider history, IIndicatorSnapshots indicators, IStockResearchSource research, IActiveSignalSource signals)
{
    public const string ContextVersion = "ctx.v1";
    public const int RecentBars = 20;
    public const int MaxNews = 10;
    public static readonly TimeSpan NewsWindow = TimeSpan.FromDays(7);

    /// <summary>Suggested levels outside last ± this fraction are rejected.</summary>
    public const decimal PriceBand = 0.20m;

    public static readonly Error InstrumentNotFound = new("instrument_not_found", "No instrument with that symbol.");
    public static readonly Error DataStale = new("data_stale", "Market data for this symbol is stale, so it can't be analysed right now.");
    public static readonly Error InsufficientHistory = new("insufficient_history", "There isn't enough price history to analyse this symbol.");

    private static readonly string[] Sentiments = ["bullish", "bearish", "neutral", "mixed"];

    private static readonly (string Name, int? Places)[] IndicatorFields =
    [
        ("sma20", 2), ("sma50", 2), ("rsi14", 1), ("macdHist", 2), ("atr14", 2), ("relVolume20", 2), ("trend", null), ("volatility20Ann", 3),
    ];

    public async Task<Result<AnalysisContext>> BuildAsync(string symbol, DateTimeOffset requestedAt, CancellationToken cancellationToken)
    {
        if (await instruments.FindAsync(symbol, cancellationToken) is not { } instrument
            || await indicators.GetDailyAsync(instrument.Symbol, cancellationToken) is not { } readout)
        {
            return InstrumentNotFound;
        }

        var quote = await research.GetQuoteAsync(instrument.Symbol, cancellationToken);
        if (quote is null || quote.Stale || readout.DataQuality == "stale")
        {
            return DataStale;
        }

        if (readout.DataQuality == "insufficient")
        {
            return InsufficientHistory;
        }

        var daily = await history.GetDailyAsync(instrument.Symbol, RecentBars, cancellationToken);
        var bars = daily?.Bars ?? [];
        var fundamentals = await research.GetFundamentalsAsync(instrument.Symbol, cancellationToken);
        var signal = await signals.GetAsync(instrument.Symbol, cancellationToken);
        var news = (await research.GetNewsAsync(instrument.Symbol, requestedAt - NewsWindow, requestedAt, MaxNews, cancellationToken))
            .Select(n => new ContextNews(n.PublishedAt, NewsSanitizer.Clean(n.Source, 80), NewsSanitizer.Clean(n.Headline, NewsSanitizer.HeadlineMax), NullIfEmpty(NewsSanitizer.Clean(n.Summary, NewsSanitizer.SnippetMax))))
            .Where(n => n.Headline.Length > 0)
            .ToList();

        var bandMin = Round(quote.Last * (1 - PriceBand), 2);
        var bandMax = Round(quote.Last * (1 + PriceBand), 2);
        var issues = new JsonArray();
        if (readout.MissingSessions.Count > 0)
        {
            issues.Add($"{readout.MissingSessions.Count} missing session(s): {string.Join(", ", readout.MissingSessions.Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))}");
        }

        var indicatorNode = new JsonObject();
        foreach (var (name, places) in IndicatorFields)
        {
            var raw = readout.Values.GetValueOrDefault(name);
            indicatorNode[name] = raw is null ? null : places is { } p ? Fixed(decimal.Parse(raw, CultureInfo.InvariantCulture), p) : raw;
        }

        var document = new JsonObject
        {
            ["contextVersion"] = ContextVersion,
            ["symbol"] = instrument.Symbol,
            ["name"] = instrument.Name,
            ["sector"] = instrument.Sector,
            ["analysisRequestedAt"] = Timestamp(requestedAt),
            ["dataAsOf"] = Timestamp(quote.AsOf),
            ["dataQuality"] = new JsonObject
            {
                ["status"] = readout.DataQuality,
                ["barCount"] = readout.BarCount,
                ["quoteQuality"] = quote.Quality,
                ["issues"] = issues,
            },
            ["quote"] = new JsonObject
            {
                ["last"] = Price(quote.Last),
                ["changePct"] = quote.PrevClose == 0 ? null : Fixed((quote.Last / quote.PrevClose) - 1, 4),
                ["volume"] = quote.Volume,
            },
            ["recentBars"] = new JsonArray(bars.TakeLast(RecentBars).Select(b => (JsonNode)new JsonObject
            {
                ["d"] = b.SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["o"] = Price(b.Open),
                ["h"] = Price(b.High),
                ["l"] = Price(b.Low),
                ["c"] = Price(b.Close),
                ["v"] = b.Volume,
            }).ToArray()),
            ["indicators"] = indicatorNode,
            ["signal"] = signal is null ? null : new JsonObject
            {
                ["type"] = signal.Type,
                ["score"] = Fixed(signal.Score, 0),
                ["reason"] = signal.Reason,
                ["levels"] = new JsonObject { ["entry"] = Price(signal.Entry), ["stop"] = Price(signal.Stop), ["target"] = Price(signal.Target) },
            },
            ["fundamentals"] = fundamentals is null ? null : new JsonObject
            {
                ["nextEarningsDate"] = fundamentals.NextEarningsDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["pe"] = fundamentals.PeRatio is { } pe ? Fixed(pe, 1) : null,
                ["beta"] = fundamentals.Beta is { } beta ? Fixed(beta, 2) : null,
            },
            ["news"] = new JsonArray(news.Select(n => (JsonNode)new JsonObject
            {
                ["publishedAt"] = Timestamp(n.PublishedAt),
                ["source"] = n.Source,
                ["headline"] = n.Headline,
                ["snippet"] = n.Snippet,
            }).ToArray()),
            ["constraints"] = new JsonObject
            {
                ["allowedSentiments"] = new JsonArray(Sentiments.Select(s => (JsonNode)s).ToArray()),
                ["priceBand"] = new JsonObject { ["min"] = Fixed(bandMin, 2), ["max"] = Fixed(bandMax, 2) },
            },
        };

        return new AnalysisContext(instrument.Symbol, instrument.Name, quote.AsOf, bandMin, bandMax, news, document);
    }

    /// <summary>UTC to the second, e.g. <c>2026-11-09T19:32:00Z</c>.</summary>
    public static string Timestamp(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Price(decimal value) => Fixed(value, Math.Abs(value) >= 1 ? 2 : 4);

    private static decimal Round(decimal value, int places) => Math.Round(value, places, MidpointRounding.ToEven);

    private static string Fixed(decimal value, int places) =>
        Round(value, places).ToString("F" + places.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
