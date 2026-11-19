using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Astryx.Contracts.MarketData;
using Astryx.Modules.Discovery.Application.Scanner;
using Astryx.Modules.Discovery.Domain;
using Astryx.Modules.Discovery.Infrastructure.Persistence;
using Astryx.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Astryx.Modules.Discovery.Application.Signals;

/// <summary>D1 (SYSTEM_DESIGN.md §11.4).</summary>
/// <param name="Status"><c>live</c>, <c>degraded</c> (last run completed with warnings) or <c>unavailable</c>.</param>
public sealed record ScannerStatusDto(string Status, int SymbolsTotal, DateTimeOffset? LastRunAt, DateTimeOffset? DataAsOf, string? ErrorCode);

public sealed record SignalMetricsDto(string? RelVolume, string? RelStrength, string? RewardRisk, string? Rsi14);

public sealed record SignalLevelsDto(string OrderType, decimal Entry, decimal Stop, decimal Target);

public sealed record SignalStrategyDto(Guid Id, string Name, int Version);

/// <summary>SignalDto (§11.4). <c>scoreBreakdown</c> and <c>proposalId</c> come with D4 only.</summary>
public sealed record SignalDto(
    Guid Id, int Rank, string Symbol, string Name, string? Sector, string Type, string Horizon, decimal? Price, decimal? ChangePct, string Score,
    SignalMetricsDto Metrics, string Reason, SignalLevelsDto Levels, SignalStrategyDto Strategy, DateTimeOffset EvaluatedAt, DateTimeOffset DataAsOf, string Status,
    JsonNode? ScoreBreakdown, Guid? ProposalId, IReadOnlyList<decimal> Sparkline, long? Volume, DateTimeOffset ExpiresAt);

public sealed record SignalCountsDto(int All, int Momentum, int Breakout, int Pullback, int UnusualVolume, int Earnings);

public sealed record ScannerRefDto(DateTimeOffset? LastRunAt, DateTimeOffset? DataAsOf);

public sealed record SignalPageDto(IReadOnlyList<SignalDto> Items, string? NextCursor, SignalCountsDto Counts, ScannerRefDto Scanner);

/// <summary>D2/D3 filters as they arrive on the query string.</summary>
public sealed record SignalFilter(string? Type, string? Sector, string? MarketCap, string? Horizon, int? MinScore, string? Sort, string? Symbol, int? Limit, string? Cursor);

/// <summary>Scanner status and the active signal list (§11.4 D1–D4). Signals are global; the same list for every user.</summary>
public sealed class SignalQueries(
    DiscoveryDbContext db, IInstrumentDirectory instruments, IPriceHistoryProvider history, IMarketDataFreshness freshness, IOptions<ScannerOptions> options, TimeProvider timeProvider)
{
    public static readonly Error NotFound = new("not_found", "No signal with that id.");

    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;

    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        ["momentum"] = "momentum",
        ["breakout"] = "breakout",
        ["pullback"] = "pullback",
        ["unusual_volume"] = "unusual_volume",
        ["unusualVolume"] = "unusual_volume",
        ["earnings"] = "earnings",
    };

    private static readonly string[] Sorts = ["score", "rr", "relStrength", "change"];

    /// <summary>
    /// D1. Unavailable when there is no finished run, the last one failed, or the scanner fell behind: market data moved
    /// on and the last run is older than two intervals (<c>SCN-504</c>). Overnight, with no new data, the last run stays
    /// current, so Discover keeps showing the close's signals.
    /// </summary>
    public async Task<ScannerStatusDto> StatusAsync(CancellationToken cancellationToken)
    {
        var last = await db.ScannerRuns.AsNoTracking().Where(r => r.Status != ScannerRunStatus.Running).OrderByDescending(r => r.StartedAt).FirstOrDefaultAsync(cancellationToken);
        if (last is null)
        {
            return new ScannerStatusDto("unavailable", 0, null, null, "SCN-404");
        }

        if (last.Status == ScannerRunStatus.Failed)
        {
            return new ScannerStatusDto("unavailable", last.SymbolsTotal, last.FinishedAt, last.DataAsOf, last.ErrorCode);
        }

        var market = await freshness.GetAsync(cancellationToken);
        var moved = market.QuotesAsOf > last.QuotesAsOf || market.DailyBarsThrough > last.BarsThrough;
        var tooOld = timeProvider.GetUtcNow() - (last.FinishedAt ?? last.StartedAt) > TimeSpan.FromMinutes(2 * options.Value.IntervalMinutes);
        if (moved && tooOld)
        {
            return new ScannerStatusDto("unavailable", last.SymbolsTotal, last.FinishedAt, last.DataAsOf, "SCN-504");
        }

        var status = last.Status == ScannerRunStatus.CompletedWithWarnings ? "degraded" : "live";
        return new ScannerStatusDto(status, last.SymbolsTotal, last.FinishedAt, last.DataAsOf, null);
    }

    /// <summary>D2. Errors: <c>bad_request</c> for an invalid filter, <c>scanner_unavailable</c> when D1 is unavailable.</summary>
    public async Task<Result<SignalPageDto>> ListAsync(SignalFilter filter, CancellationToken cancellationToken)
    {
        if (Validate(filter) is { } invalid)
        {
            return invalid;
        }

        var scanner = await StatusAsync(cancellationToken);
        if (scanner.Status == "unavailable")
        {
            return Unavailable(scanner);
        }

        var rows = await LoadAsync(cancellationToken);
        var withoutType = rows.Where(r => Matches(r, filter with { Type = null })).ToList();
        var counts = new SignalCountsDto(
            withoutType.Count,
            withoutType.Count(r => r.Signal.SignalType == "momentum"),
            withoutType.Count(r => r.Signal.SignalType == "breakout"),
            withoutType.Count(r => r.Signal.SignalType == "pullback"),
            withoutType.Count(r => r.Signal.SignalType == "unusual_volume"),
            withoutType.Count(r => r.Signal.SignalType == "earnings"));

        var sorted = Sort(withoutType.Where(r => Matches(r, filter)), filter.Sort ?? "score").ToList();
        var offset = DecodeCursor(filter.Cursor);
        var limit = filter.Limit ?? DefaultLimit;
        var shown = sorted.Skip(offset).Take(limit).ToList();
        var trails = await history.GetTrailsAsync(shown.Select(r => r.Signal.Symbol).ToList(), SparklineSessions, cancellationToken); // one query for the page
        var page = shown.Select((row, i) => ToDto(row, offset + i + 1, withBreakdown: false, Trail(trails, row.Signal.Symbol))).ToList();

        var next = offset + limit < sorted.Count ? EncodeCursor(offset + limit) : null;
        return new SignalPageDto(page, next, counts, new ScannerRefDto(scanner.LastRunAt, scanner.DataAsOf));
    }

    /// <summary>D3: the live count for "Show N setups".</summary>
    public async Task<Result<int>> CountAsync(SignalFilter filter, CancellationToken cancellationToken)
    {
        if (Validate(filter) is { } invalid)
        {
            return invalid;
        }

        var scanner = await StatusAsync(cancellationToken);
        if (scanner.Status == "unavailable")
        {
            return Unavailable(scanner);
        }

        return (await LoadAsync(cancellationToken)).Count(r => Matches(r, filter));
    }

    /// <summary>D4: any signal by id (also superseded or expired, for history), with its score breakdown.</summary>
    public async Task<Result<SignalDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await db.Signals.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, cancellationToken) is not { } signal)
        {
            return NotFound;
        }

        var summary = (await instruments.GetAsync([signal.InstrumentId], cancellationToken)).SingleOrDefault();
        var rank = signal.Status == SignalStatus.Active
            ? await db.Signals.CountAsync(s => s.Status == SignalStatus.Active && s.Score > signal.Score, cancellationToken) + 1
            : 0;
        return ToDto(new Row(signal, summary), rank, withBreakdown: true, Trail(await history.GetTrailsAsync([signal.Symbol], SparklineSessions, cancellationToken), signal.Symbol));
    }

    private sealed record Row(Signal Signal, InstrumentSummary? Instrument);

    private async Task<List<Row>> LoadAsync(CancellationToken cancellationToken)
    {
        var signals = await db.Signals.AsNoTracking().Where(s => s.Status == SignalStatus.Active).ToListAsync(cancellationToken);
        var summaries = (await instruments.GetAsync(signals.Select(s => s.InstrumentId).Distinct().ToList(), cancellationToken)).ToDictionary(s => s.Id);
        return signals.Select(s => new Row(s, summaries.GetValueOrDefault(s.InstrumentId))).ToList();
    }

    private static Error? Validate(SignalFilter filter)
    {
        if (filter.Type is { } type && !Types.ContainsKey(type))
        {
            return new Error("bad_request", "type must be momentum, breakout, pullback, unusual_volume or earnings.");
        }

        if (filter.MarketCap is { } cap && cap is not ("mega" or "large" or "mid" or "small"))
        {
            return new Error("bad_request", "marketCap must be mega, large, mid or small.");
        }

        if (filter.Horizon is { } horizon && horizon is not ("intraday" or "swing" or "position"))
        {
            return new Error("bad_request", "horizon must be intraday, swing or position.");
        }

        if (filter.Sort is { } sort && !Sorts.Contains(sort))
        {
            return new Error("bad_request", "sort must be score, rr, relStrength or change.");
        }

        if (filter.MinScore is < 0 or > 100 || filter.Limit is < 1 or > MaxLimit)
        {
            return new Error("bad_request", $"minScore must be 0–100 and limit 1–{MaxLimit}.");
        }

        return filter.Cursor is { } cursor && DecodeCursor(cursor) < 0 ? new Error("bad_request", "cursor is not valid.") : null;
    }

    private static Error Unavailable(ScannerStatusDto scanner) =>
        new("scanner_unavailable", $"The scanner feed is unavailable ({scanner.ErrorCode}). Signals are hidden rather than shown stale.");

    private static bool Matches(Row row, SignalFilter filter)
    {
        var s = row.Signal;
        var cap = row.Instrument?.MarketCap;
        return (filter.Type is null || s.SignalType == Types[filter.Type])
            && (filter.Sector is null || string.Equals(row.Instrument?.Sector, filter.Sector, StringComparison.OrdinalIgnoreCase) || string.Equals(row.Instrument?.Industry, filter.Sector, StringComparison.OrdinalIgnoreCase))
            && (filter.MarketCap is null || filter.MarketCap switch
            {
                "mega" => cap >= 200_000_000_000m,
                "large" => cap is >= 10_000_000_000m and < 200_000_000_000m,
                "mid" => cap is >= 2_000_000_000m and < 10_000_000_000m,
                _ => cap < 2_000_000_000m,
            })
            && (filter.Horizon is null || s.Horizon == filter.Horizon)
            && (filter.MinScore is null || s.Score >= filter.MinScore)
            && (filter.Symbol is null || string.Equals(s.Symbol, filter.Symbol.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Descending by the chosen value; ties by score, then symbol, so pages are stable.</summary>
    private static IEnumerable<Row> Sort(IEnumerable<Row> rows, string sort) =>
        (sort switch
        {
            "rr" => rows.OrderByDescending(r => Metric(r.Signal, "rewardRisk")),
            "relStrength" => rows.OrderByDescending(r => Metric(r.Signal, "relStrength")),
            "change" => rows.OrderByDescending(r => r.Instrument?.ChangePct ?? decimal.MinValue),
            _ => rows.OrderByDescending(r => r.Signal.Score),
        }).ThenByDescending(r => r.Signal.Score).ThenBy(r => r.Signal.Symbol, StringComparer.Ordinal);

    private static decimal Metric(Signal signal, string key) =>
        JsonNode.Parse(signal.Metrics)?[key]?.GetValue<string>() is { } text ? decimal.Parse(text, CultureInfo.InvariantCulture) : decimal.MinValue;

    /// <summary>Sessions in a row's sparkline (design 02).</summary>
    public const int SparklineSessions = 20;

    /// <summary>The last 20 daily closes and today's volume, for the row's sparkline and "412M".</summary>
    private static (IReadOnlyList<decimal> Closes, long? Volume) Trail(IReadOnlyDictionary<string, PriceTrail> trails, string symbol) =>
        trails.TryGetValue(symbol.ToUpperInvariant(), out var trail) ? (trail.Closes.Select(Price).ToList(), trail.Volume) : ([], null);

    private static SignalDto ToDto(Row row, int rank, bool withBreakdown, (IReadOnlyList<decimal> Closes, long? Volume) trail)
    {
        var s = row.Signal;
        var metrics = JsonNode.Parse(s.Metrics)!;
        string? M(string key) => metrics[key]?.GetValue<string>();
        return new SignalDto(
            s.Id, rank, s.Symbol, row.Instrument?.Name ?? s.Symbol, row.Instrument?.Industry ?? row.Instrument?.Sector, s.SignalType, s.Horizon,
            row.Instrument?.Last, row.Instrument?.ChangePct, decimal.Round(s.Score).ToString(CultureInfo.InvariantCulture),
            new SignalMetricsDto(M("relVolume"), M("relStrength"), M("rewardRisk"), M("rsi14")),
            s.Reason,
            new SignalLevelsDto(s.OrderType, Price(s.EntryPrice), Price(s.StopPrice), Price(s.TargetPrice)),
            new SignalStrategyDto(s.StrategyId, s.StrategyName, s.StrategyVersion),
            s.EvaluatedAt, s.DataAsOf, s.Status,
            withBreakdown ? JsonNode.Parse(s.ScoreBreakdown) : null,
            ProposalId: null,
            trail.Closes,
            trail.Volume,
            s.ExpiresAt);
    }

    /// <summary>Stored at 6 dp; prices go out at cents (four places below $1).</summary>
    private static decimal Price(decimal value) => Math.Round(value, Math.Abs(value) >= 1 ? 2 : 4, MidpointRounding.AwayFromZero);

    private static string EncodeCursor(int offset) => Convert.ToBase64String(Encoding.UTF8.GetBytes($"o:{offset}"));

    /// <summary>0 for no cursor, -1 for one that can't be read.</summary>
    private static int DecodeCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return 0;
        }

        try
        {
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            return text.StartsWith("o:", StringComparison.Ordinal) && int.TryParse(text[2..], NumberStyles.None, CultureInfo.InvariantCulture, out var offset) ? offset : -1;
        }
        catch (FormatException)
        {
            return -1;
        }
    }
}
