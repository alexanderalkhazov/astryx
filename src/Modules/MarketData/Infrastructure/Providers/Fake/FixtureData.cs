using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Astryx.Modules.MarketData.Infrastructure.Providers.Fake;

/// <summary>
/// The fake provider's fixtures, embedded from <c>tests/fixtures/market/</c> so the Development container needs no
/// extra files (SYSTEM_DESIGN.md §16.1, §21.1).
/// </summary>
internal sealed class FixtureData
{
    private const string ResourcePrefix = "Astryx.Fixtures.Market.";

    private FixtureData(IReadOnlyList<FixtureInstrument> instruments, IReadOnlyList<FixtureCalendarDay> calendar, IReadOnlyList<FixtureCorporateAction> actions, IReadOnlyList<FixtureNews> news)
    {
        Instruments = instruments;
        BySymbol = instruments.ToDictionary(i => i.Symbol, StringComparer.OrdinalIgnoreCase);
        Calendar = calendar.ToDictionary(d => d.Date);
        CorporateActions = actions;
        News = news;
    }

    public IReadOnlyList<FixtureInstrument> Instruments { get; }

    public IReadOnlyDictionary<string, FixtureInstrument> BySymbol { get; }

    public IReadOnlyDictionary<DateOnly, FixtureCalendarDay> Calendar { get; }

    public IReadOnlyList<FixtureCorporateAction> CorporateActions { get; }

    public IReadOnlyList<FixtureNews> News { get; }

    public static FixtureData Load()
    {
        var instruments = ReadCsv("universe.csv").Select(r => new FixtureInstrument(
            r["symbol"], r["exchange_mic"], r["name"], r["asset_type"], NullIfEmpty(r["sector"]), NullIfEmpty(r["industry"]),
            OptionalDecimal(r["market_cap"]), decimal.Parse(r["last"], CultureInfo.InvariantCulture), decimal.Parse(r["prev_close"], CultureInfo.InvariantCulture),
            OptionalDecimal(r["open"]), OptionalDecimal(r["high"]), OptionalDecimal(r["low"]), long.Parse(r["volume"], CultureInfo.InvariantCulture),
            long.Parse(r["avg_volume"], CultureInfo.InvariantCulture), r["pattern"], OptionalDecimal(r["level"]), OptionalDecimal(r["beta"]),
            OptionalDecimal(r["pe_ratio"]), OptionalDecimal(r["eps_ttm"]), string.IsNullOrEmpty(r["earnings_in_days"]) ? null : int.Parse(r["earnings_in_days"], CultureInfo.InvariantCulture))).ToList();

        var calendar = ReadCsv("calendar.csv").Select(r => new FixtureCalendarDay(DateOnly.ParseExact(r["date"], "yyyy-MM-dd", CultureInfo.InvariantCulture), r["kind"] == "half_day")).ToList();

        var actions = ReadCsv("corporate_actions.csv").Select(r => new FixtureCorporateAction(
            r["symbol"], r["action_type"], DateOnly.ParseExact(r["ex_date"], "yyyy-MM-dd", CultureInfo.InvariantCulture), OptionalDecimal(r["ratio"]), OptionalDecimal(r["cash_amount"]))).ToList();

        using var newsStream = Open("news.json");
        var news = JsonSerializer.Deserialize<NewsFile>(newsStream, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Items;

        return new FixtureData(instruments, calendar, actions, news);
    }

    private static Stream Open(string file) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourcePrefix + file)
        ?? throw new InvalidOperationException($"Embedded market fixture '{file}' is missing.");

    /// <summary>Minimal RFC 4180 reader: quoted fields may contain commas and doubled quotes.</summary>
    private static IEnumerable<Dictionary<string, string>> ReadCsv(string file)
    {
        using var reader = new StreamReader(Open(file));
        var header = SplitLine(reader.ReadLine() ?? string.Empty);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var fields = SplitLine(line);
            yield return header.Select((name, i) => (name, value: i < fields.Count ? fields[i] : string.Empty)).ToDictionary(p => p.name, p => p.value);
        }
    }

    private static List<string> SplitLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private static decimal? OptionalDecimal(string value) => value.Length == 0 ? null : decimal.Parse(value, CultureInfo.InvariantCulture);

    private sealed record NewsFile(List<FixtureNews> Items);
}

/// <summary>One universe row. <c>Last</c>, <c>PrevClose</c> and the optional session values describe the current session.</summary>
internal sealed record FixtureInstrument(
    string Symbol,
    string ExchangeMic,
    string Name,
    string AssetType,
    string? Sector,
    string? Industry,
    decimal? MarketCap,
    decimal Last,
    decimal PrevClose,
    decimal? Open,
    decimal? High,
    decimal? Low,
    long Volume,
    long AverageVolume,
    string Pattern,
    decimal? Level,
    decimal? Beta,
    decimal? PeRatio,
    decimal? EpsTtm,
    int? EarningsInDays);

internal sealed record FixtureCalendarDay(DateOnly Date, bool IsHalfDay);

internal sealed record FixtureCorporateAction(string Symbol, string ActionType, DateOnly ExDate, decimal? Ratio, decimal? CashAmount);

internal sealed record FixtureNews(string Id, List<string> Symbols, string Source, int MinutesAgo, string Headline, string? Summary, bool Injection);
