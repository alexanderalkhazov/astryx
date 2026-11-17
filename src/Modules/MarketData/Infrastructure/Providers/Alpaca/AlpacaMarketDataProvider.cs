using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Astryx.Infrastructure.Alpaca;
using Astryx.Modules.MarketData.Application.Providers;
using Astryx.Modules.MarketData.Domain;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Astryx.Modules.MarketData.Infrastructure.Providers.Alpaca;

/// <summary>
/// Real US equity data from Alpaca's Market Data API (SYSTEM_DESIGN.md §16.2, §17.3, TASKS.md T-147), on the free Basic
/// plan:
/// <list type="bullet">
/// <item>last, bid and ask come from the real-time IEX feed, so quote freshness is measured in seconds;</item>
/// <item>the day's open, high, low, volume and the previous close come from the 15-minute-delayed consolidated feed
/// (<c>delayed_sip</c>), because IEX alone carries only a few percent of US volume;</item>
/// <item>history comes from the consolidated feed (<c>sip</c>), which the plan serves up to 15 minutes ago, unadjusted
/// (the app adjusts it, §20.7).</item>
/// </list>
/// The universe is the S&amp;P 500 with GICS sectors (<c>us_large_cap.csv</c>), because Alpaca has no sector data and no
/// fundamentals.
/// </summary>
internal sealed class AlpacaMarketDataProvider(IHttpClientFactory httpFactory, AlpacaRateLimiter limiter, IOptions<AlpacaOptions> options, AlpacaUniverse universe, TimeProvider timeProvider)
    : IMarketDataProvider
{
    public const string ProviderCode = "alpaca";
    public const string HttpClientName = AlpacaHttpClient.Name;

    /// <summary>The consolidated feed is available only up to this long ago on the Basic plan.</summary>
    private static readonly TimeSpan SipDelay = TimeSpan.FromMinutes(16);

    private static readonly DateTimeZone NewYork = DateTimeZoneProviders.Tzdb["America/New_York"];
    private static readonly LocalTime SessionOpen = new(9, 30);
    private static readonly LocalTime SessionClose = new(16, 0);

    public string Code => ProviderCode;

    public MarketDataCapabilities Capabilities { get; } = new(
        Realtime: true,
        IntradayIntervals: [BarInterval.OneMinute, BarInterval.FiveMinutes, BarInterval.FifteenMinutes, BarInterval.OneHour],
        Fundamentals: false,
        CorporateActions: true,
        MaxSymbolsPerRequest: 200);

    private AlpacaCredentials Credentials => options.Value.Credentials
        ?? throw new MarketDataUnavailableException(ProviderCode, "Alpaca:KeyId and Alpaca:SecretKey are not configured");

    private Uri Data(string pathAndQuery) => new(options.Value.DataBaseUrl.TrimEnd('/') + pathAndQuery);

    public async Task<IReadOnlyList<ProviderQuote>> GetQuotesAsync(IReadOnlyList<string> symbols, CancellationToken cancellationToken)
    {
        if (symbols.Count == 0)
        {
            return [];
        }

        using var live = await SnapshotsAsync(symbols, "iex", cancellationToken);
        using var delayed = await SnapshotsAsync(symbols, "delayed_sip", cancellationToken);

        var quotes = new List<ProviderQuote>(symbols.Count);
        foreach (var symbol in symbols)
        {
            if (!live.RootElement.TryGetProperty(symbol, out var realtime) || realtime.ValueKind != JsonValueKind.Object
                || !delayed.RootElement.TryGetProperty(symbol, out var consolidated) || consolidated.ValueKind != JsonValueKind.Object
                || realtime.Object("latestTrade") is not { } trade || trade.Decimal("p") is not { } last || last <= 0
                || consolidated.Object("dailyBar") is not { } day || consolidated.Object("prevDailyBar") is not { } previous
                || previous.Decimal("c") is not { } prevClose)
            {
                continue; // providers omit what they cannot price rather than failing the batch
            }

            var asOf = trade.Time("t") ?? timeProvider.GetUtcNow();
            decimal? bid = null, ask = null;
            if (realtime.Object("latestQuote") is { } quote && quote.Decimal("bp") is > 0 and var bp && quote.Decimal("ap") is > 0 and var ap
                && ap >= bp && (ap - bp) / last <= 0.02m)
            {
                (bid, ask) = (bp, ap); // a one-sided or very wide IEX quote is not a usable spread
                if (quote.Time("t") is { } quotedAt && quotedAt > asOf)
                {
                    asOf = quotedAt;
                }
            }

            var open = day.Decimal("o") ?? last;
            var high = Math.Max(day.Decimal("h") ?? last, last);
            var low = Math.Min(day.Decimal("l") ?? last, last);
            var volume = (long)(day.Decimal("v") ?? 0m);
            quotes.Add(new ProviderQuote(symbol, last, bid, ask, open, high, low, prevClose, volume, asOf, QuoteQuality.Realtime));
        }

        return quotes;
    }

    /// <summary>
    /// One snapshot batch. Alpaca rejects a whole batch over one unknown symbol (a delisting, a ticker change), so the
    /// symbol it names is dropped and the rest retried; that symbol simply gets no quote.
    /// </summary>
    private async Task<JsonDocument> SnapshotsAsync(IReadOnlyList<string> symbols, string feed, CancellationToken cancellationToken)
    {
        var remaining = symbols.ToList();
        for (var attempt = 0; ; attempt++)
        {
            var uri = Data($"/v2/stocks/snapshots?symbols={Uri.EscapeDataString(string.Join(',', remaining))}&feed={feed}");
            try
            {
                return await AlpacaHttp.SendAsync(httpFactory.CreateClient(HttpClientName), limiter, Credentials, HttpMethod.Get, uri, body: null, cancellationToken);
            }
            catch (AlpacaApiException ex) when (ex.Failure == AlpacaFailure.Invalid && attempt < 5 && InvalidSymbol(ex.Message) is { } bad && remaining.Remove(bad) && remaining.Count > 0)
            {
            }
            catch (AlpacaApiException ex)
            {
                throw new MarketDataUnavailableException(ProviderCode, ex.Message);
            }
        }
    }

    private static string? InvalidSymbol(string message)
    {
        const string marker = "invalid symbol: ";
        var at = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return at < 0 ? null : message[(at + marker.Length)..].Trim().TrimEnd('"', '}', '.');
    }

    public async Task<IReadOnlyList<ProviderBar>> GetBarsAsync(string symbol, BarInterval interval, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var start = AtNewYork(from, LocalTime.Midnight);
        var end = AtNewYork(to.AddDays(1), LocalTime.Midnight);
        var latest = now - SipDelay;
        if (end > latest)
        {
            end = latest;
        }

        if (end <= start)
        {
            return [];
        }

        var timeframe = interval switch
        {
            BarInterval.OneMinute => "1Min",
            BarInterval.FiveMinutes => "5Min",
            BarInterval.FifteenMinutes => "15Min",
            BarInterval.OneHour => "1Hour",
            _ => "1Day",
        };

        var bars = new List<ProviderBar>();
        string? page = null;
        do
        {
            var query = $"/v2/stocks/bars?symbols={Uri.EscapeDataString(symbol)}&timeframe={timeframe}&start={Rfc3339(start)}&end={Rfc3339(end)}&feed=sip&adjustment=raw&limit=10000"
                + (page is null ? string.Empty : $"&page_token={Uri.EscapeDataString(page)}");
            using var json = await SendAsync(Data(query), cancellationToken);
            if (json.RootElement.Object("bars") is { } bySymbol && bySymbol.TryGetProperty(symbol, out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (ToBar(symbol, interval, item, now) is { } bar)
                    {
                        bars.Add(bar);
                    }
                }
            }

            page = json.RootElement.String("next_page_token");
        }
        while (page is not null);

        return bars;
    }

    private static ProviderBar? ToBar(string symbol, BarInterval interval, JsonElement item, DateTimeOffset now)
    {
        if (item.Time("t") is not { } t || item.Decimal("o") is not { } open || item.Decimal("h") is not { } high
            || item.Decimal("l") is not { } low || item.Decimal("c") is not { } close)
        {
            return null;
        }

        var volume = (long)(item.Decimal("v") ?? 0m);
        var local = Instant.FromDateTimeOffset(t).InZone(NewYork);
        var session = local.Date.ToDateOnly();
        if (interval == BarInterval.OneDay)
        {
            // Daily bars are stamped at midnight New York time; the app's daily bar starts at the open (as the fake does).
            var openAt = AtNewYork(session, SessionOpen);
            var final = now >= AtNewYork(session, SessionClose) + SipDelay;
            return new ProviderBar(symbol, interval, openAt, session, open, high, low, close, volume, final);
        }

        // Intraday bars include pre- and post-market trading; the app keeps the regular session only.
        if (local.TimeOfDay < SessionOpen || local.TimeOfDay >= SessionClose)
        {
            return null;
        }

        return new ProviderBar(symbol, interval, t, session, open, high, low, close, volume, IsFinal: t + TimeSpan.FromMinutes((long)interval.Minutes()) <= now - SipDelay);
    }

    public Task<IReadOnlyList<ProviderInstrument>> GetInstrumentsAsync(string universeCode, CancellationToken cancellationToken)
    {
        IReadOnlyList<ProviderInstrument> instruments = universeCode == AlpacaUniverse.Code
            ? universe.Members.Select(m => new ProviderInstrument(m.Symbol, m.ExchangeMic, m.Name, AssetType.Equity, "USD", m.Sector, m.Industry, MarketCap: null, m.Symbol)).ToList()
            : [];
        return Task.FromResult(instruments);
    }

    public async Task<IReadOnlyList<ProviderCorporateAction>> GetCorporateActionsAsync(string symbol, DateOnly from, CancellationToken cancellationToken)
    {
        var actions = new List<ProviderCorporateAction>();
        string? page = null;
        do
        {
            var query = $"/v1/corporate-actions?symbols={Uri.EscapeDataString(symbol)}&types=forward_split,reverse_split,cash_dividend&start={from:yyyy-MM-dd}&limit=1000"
                + (page is null ? string.Empty : $"&page_token={Uri.EscapeDataString(page)}");
            using var json = await SendAsync(Data(query), cancellationToken);
            if (json.RootElement.Object("corporate_actions") is { } groups)
            {
                actions.AddRange(Splits(groups, "forward_splits", CorporateActionType.Split, symbol));
                actions.AddRange(Splits(groups, "reverse_splits", CorporateActionType.ReverseSplit, symbol));
                if (groups.TryGetProperty("cash_dividends", out var dividends) && dividends.ValueKind == JsonValueKind.Array)
                {
                    foreach (var d in dividends.EnumerateArray())
                    {
                        if (ExDate(d) is { } exDate && d.Decimal("rate") is > 0 and var rate)
                        {
                            actions.Add(new ProviderCorporateAction(symbol, CorporateActionType.CashDividend, exDate, null, rate));
                        }
                    }
                }
            }

            page = json.RootElement.String("next_page_token");
        }
        while (page is not null);

        return actions.OrderBy(a => a.ExDate).ToList();
    }

    private static IEnumerable<ProviderCorporateAction> Splits(JsonElement groups, string name, string type, string symbol)
    {
        if (!groups.TryGetProperty(name, out var items) || items.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var s in items.EnumerateArray())
        {
            // A ratio is new shares per old share, so a 1-for-10 reverse split is 0.1 (§20.7).
            if (ExDate(s) is { } exDate && s.Decimal("new_rate") is > 0 and var newRate && s.Decimal("old_rate") is > 0 and var oldRate)
            {
                yield return new ProviderCorporateAction(symbol, type, exDate, newRate / oldRate, null);
            }
        }
    }

    private static DateOnly? ExDate(JsonElement element) =>
        element.String("ex_date") is { } text && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    /// <summary>Alpaca has no fundamentals; the capability is off, so the sync never asks.</summary>
    public Task<ProviderFundamentals?> GetFundamentalsAsync(string symbol, CancellationToken cancellationToken) => Task.FromResult<ProviderFundamentals?>(null);

    public async Task<IReadOnlyList<ProviderSession>> GetCalendarAsync(string exchangeMic, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (!AlpacaUniverse.UsEquityMics.Contains(exchangeMic))
        {
            return [];
        }

        var uri = new Uri(options.Value.TradingBaseUrl(AlpacaEnvironment.Paper), $"v2/calendar?start={from:yyyy-MM-dd}&end={to:yyyy-MM-dd}");
        using var json = await SendAsync(uri, cancellationToken);
        var trading = new Dictionary<DateOnly, ProviderSession>();
        foreach (var day in json.RootElement.EnumerateArray())
        {
            if (ExDate(day, "date") is { } date && LocalTimeOf(day.String("open")) is { } open && LocalTimeOf(day.String("close")) is { } close)
            {
                trading[date] = new ProviderSession(exchangeMic, date, AtNewYork(date, open), AtNewYork(date, close), IsHalfDay: close < SessionClose, IsHoliday: false);
            }
        }

        // Weekdays Alpaca leaves out are market holidays; the calendar table keeps a row for them (§16.3).
        var sessions = new List<ProviderSession>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (trading.TryGetValue(date, out var session))
            {
                sessions.Add(session);
            }
            else if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                sessions.Add(new ProviderSession(exchangeMic, date, null, null, IsHalfDay: false, IsHoliday: true));
            }
        }

        return sessions;
    }

    private static DateOnly? ExDate(JsonElement element, string name) =>
        element.String(name) is { } text && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static LocalTime? LocalTimeOf(string? text) =>
        text is not null && TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? LocalTime.FromTimeOnly(time) : null;

    internal static DateTimeOffset AtNewYork(DateOnly date, LocalTime time) =>
        LocalDate.FromDateOnly(date).At(time).InZoneLeniently(NewYork).ToDateTimeOffset().ToUniversalTime();

    private static string Rfc3339(DateTimeOffset at) => Uri.EscapeDataString(at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));

    private Task<JsonDocument> SendAsync(Uri uri, CancellationToken cancellationToken) => SendAsync(httpFactory, limiter, Credentials, uri, cancellationToken);

    internal static async Task<JsonDocument> SendAsync(IHttpClientFactory httpFactory, AlpacaRateLimiter limiter, AlpacaCredentials credentials, Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            return await AlpacaHttp.SendAsync(httpFactory.CreateClient(HttpClientName), limiter, credentials, HttpMethod.Get, uri, body: null, cancellationToken);
        }
        catch (AlpacaApiException ex)
        {
            throw new MarketDataUnavailableException(ProviderCode, ex.Message);
        }
    }
}

/// <summary>Alpaca news (Benzinga and others) for the stock screen's news list (SYSTEM_DESIGN.md §16.2).</summary>
internal sealed class AlpacaNewsProvider(IHttpClientFactory httpFactory, AlpacaRateLimiter limiter, IOptions<AlpacaOptions> options) : INewsProvider
{
    public async Task<IReadOnlyList<ProviderNewsItem>> GetNewsAsync(string symbol, DateTimeOffset since, int limit, CancellationToken cancellationToken)
    {
        var credentials = options.Value.Credentials
            ?? throw new MarketDataUnavailableException(AlpacaMarketDataProvider.ProviderCode, "Alpaca:KeyId and Alpaca:SecretKey are not configured");
        var start = Uri.EscapeDataString(since.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        var uri = new Uri(options.Value.DataBaseUrl.TrimEnd('/') + $"/v1beta1/news?symbols={Uri.EscapeDataString(symbol)}&start={start}&limit={Math.Clamp(limit, 1, 50)}&sort=desc");
        using var json = await AlpacaMarketDataProvider.SendAsync(httpFactory, limiter, credentials, uri, cancellationToken);
        var items = new List<ProviderNewsItem>();
        if (json.RootElement.TryGetProperty("news", out var news) && news.ValueKind == JsonValueKind.Array)
        {
            foreach (var n in news.EnumerateArray())
            {
                if (n.TryGetProperty("id", out var id) && n.String("headline") is { Length: > 0 } headline && n.String("url") is { Length: > 0 } url && n.Time("created_at") is { } published)
                {
                    var symbols = n.TryGetProperty("symbols", out var s) && s.ValueKind == JsonValueKind.Array
                        ? s.EnumerateArray().Select(x => x.GetString()!).Where(x => !string.IsNullOrEmpty(x)).ToList()
                        : [symbol];
                    items.Add(new ProviderNewsItem(id.ToString(), n.String("source") ?? "alpaca", headline, url, published, n.String("summary") is { Length: > 0 } summary ? summary : null, symbols));
                }
            }
        }

        return items;
    }
}

/// <summary>
/// The scanner universe for real data: S&amp;P 500 members tradable at Alpaca, with GICS sector and sub-industry, from
/// Wikipedia's constituent list (CC BY-SA, 2026-11-17). Refreshing it is a data change, not a code change.
/// </summary>
internal sealed class AlpacaUniverse
{
    public const string Code = "us_large_cap";

    public static readonly IReadOnlySet<string> UsEquityMics = new HashSet<string>(StringComparer.Ordinal) { "XNYS", "XNAS", "ARCX" };

    public sealed record Member(string Symbol, string ExchangeMic, string Name, string Sector, string Industry);

    public IReadOnlyList<Member> Members { get; }

    private AlpacaUniverse(IReadOnlyList<Member> members) => Members = members;

    public static AlpacaUniverse Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Astryx.Reference.Alpaca.us_large_cap.csv")
            ?? throw new InvalidOperationException("The Alpaca universe resource is missing.");
        using var reader = new StreamReader(stream);
        reader.ReadLine(); // header
        var members = new List<Member>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0)
            {
                var cells = SplitCsv(line);
                members.Add(new Member(cells[0], cells[1], cells[2], cells[3], cells[4]));
            }
        }

        return new AlpacaUniverse(members);
    }

    private static List<string> SplitCsv(string line)
    {
        var cells = new List<string>();
        var cell = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (c == ',' && !quoted)
            {
                cells.Add(cell.ToString());
                cell.Clear();
            }
            else
            {
                cell.Append(c);
            }
        }

        cells.Add(cell.ToString());
        return cells;
    }
}
