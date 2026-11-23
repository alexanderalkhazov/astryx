using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Astryx.Contracts.Discovery;
using Astryx.Modules.Strategies.Domain;
using Astryx.Modules.Strategies.Infrastructure.Persistence;
using Astryx.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Strategies.Application;

/// <summary>Headline backtest metrics, as decimal strings (fractions: 0.169 = 16.9 %).</summary>
public sealed record StrategyMetricsDto(string Cagr, string Sharpe, string MaxDrawdown, string WinRate);

/// <summary>Live paper results. Illustrative (seeded) until Portfolio attributes fills to strategies (M5).</summary>
public sealed record LiveResultsDto(string SinceInceptionPct, string MtdPct, string MtdPnl, int MtdTrades, bool Illustrative);

/// <summary>Drawdown guard state (§7.8): drawdown from the window's high, the warning and pause thresholds (fractions).</summary>
public sealed record GuardDto(string Drawdown, string WarnAt, string PauseAt, int WindowDays, DateOnly AsOf);

/// <summary>ST1 item (SYSTEM_DESIGN.md §11.9).</summary>
/// <param name="Sparkline">Equity of the latest run, downsampled to at most 60 points, as decimal strings.</param>
public sealed record StrategySummaryDto(
    Guid Id, string Code, string Name, string Description, string Status, int DisplayOrder, int Version,
    DateOnly? LiveSince, LiveResultsDto? Live, StrategyMetricsDto? Metrics, IReadOnlyList<string> Sparkline, DateOnly? BacktestStart, DateOnly? BacktestEnd, GuardDto? Guard,
    bool BacktestIllustrative = true);

public sealed record ParameterDto(string Key, string Label, string Unit, JsonNode? Default, JsonNode? Min, JsonNode? Max, JsonNode? Step, JsonNode? Value);

public sealed record VersionDto(Guid Id, int Number, string State, string CodeVersion, IReadOnlyList<ParameterDto> Parameters);

/// <summary>Run metrics for one segment, as decimal strings.</summary>
public sealed record RunSummaryDto(Guid Id, int RunNumber, string Status, DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly OosStart, DateTimeOffset? FinishedAt, JsonNode Metrics);

/// <summary>ST3.</summary>
/// <param name="TodaysSignals">The strategy's active scanner signals, best first.</param>
public sealed record StrategyDetailDto(StrategySummaryDto Summary, IReadOnlyList<string> Rules, VersionDto Version, RunSummaryDto? LatestRun, IReadOnlyList<StrategySignalDto> TodaysSignals);

public sealed record StrategySignalDto(Guid Id, string Symbol, string Type, decimal Score, decimal Entry, DateTimeOffset EvaluatedAt);

/// <summary>ST4: a full run with its curves. Results are illustrative of history, never a forecast.</summary>
public sealed record BacktestRunDto(
    Guid Id, Guid StrategyId, int RunNumber, int Version, string Status, DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly OosStart, decimal InitialCapital,
    JsonNode Assumptions, JsonNode Metrics, JsonNode EquityCurve, JsonNode DrawdownCurve, JsonNode MonthlyReturns, DateTimeOffset? FinishedAt, string? ErrorCode);

/// <summary>One strategy's growth of $1 on its latest run (ST2), sampled as in the run's equity curve.</summary>
public sealed record CompareSeriesDto(Guid StrategyId, string Name, IReadOnlyList<ComparePointDto> Points);

public sealed record ComparePointDto(DateOnly D, string Growth);

/// <summary>ST2: overlay series and the full-period metrics of each latest run, side by side. Illustrative backtests.</summary>
public sealed record CompareDto(IReadOnlyList<CompareSeriesDto> Series, IReadOnlyList<CompareMetricsDto> Metrics);

public sealed record CompareMetricsDto(Guid StrategyId, string Name, JsonNode? Metrics);

/// <summary>ST5 item.</summary>
public sealed record StrategyPeriodDto(Guid StrategyId, string Name, string Pnl, string PnlPct, int Trades, bool Illustrative);

/// <summary>The system strategy catalog (SYSTEM_DESIGN.md §7.8, §11.9 ST1, ST3–ST5). Read-only in the MVP.</summary>
public sealed class StrategyCatalog(StrategiesDbContext db, IStrategySignalFeed signals)
{
    public static readonly Error NotFound = new("not_found", "No strategy with that id.");
    public static readonly Error RunNotFound = new("not_found", "No backtest run with that id.");
    public static readonly Error UnsupportedPeriod = new("bad_request", "Only period=mtd is supported.");
    public static readonly Error BadCompare = new("validation_failed", "ids must name 1–5 strategies.");

    public const int SparklinePoints = 60;

    public async Task<IReadOnlyList<StrategySummaryDto>> ListAsync(CancellationToken cancellationToken)
    {
        var strategies = await SystemStrategies().OrderBy(s => s.DisplayOrder).ToListAsync(cancellationToken);
        var versions = await PublishedVersionsAsync(strategies, cancellationToken);
        var runs = await LatestRunsAsync(strategies.Select(s => s.Id).ToList(), cancellationToken);
        return strategies.Select(s => Summary(s, versions.GetValueOrDefault(s.Id), runs.GetValueOrDefault(s.Id))).ToList();
    }

    /// <summary>ST3. <paramref name="idOrCode"/> is the strategy id or its code (<c>base_breakout</c>).</summary>
    public async Task<Result<StrategyDetailDto>> GetAsync(string idOrCode, CancellationToken cancellationToken)
    {
        if (await FindAsync(idOrCode, cancellationToken) is not { } strategy)
        {
            return NotFound;
        }

        var version = (await PublishedVersionsAsync([strategy], cancellationToken)).GetValueOrDefault(strategy.Id);
        if (version is null)
        {
            return NotFound;
        }

        var run = (await LatestRunsAsync([strategy.Id], cancellationToken)).GetValueOrDefault(strategy.Id);
        var rules = JsonSerializer.Deserialize<List<string>>(strategy.Rules) ?? [];
        var values = JsonNode.Parse(version.Params)!.AsObject();
        var parameters = JsonNode.Parse(strategy.ParamSchema)!.AsArray()
            .Select(p => new ParameterDto(
                (string)p!["key"]!, (string)p["label"]!, (string)p["unit"]!, p["default"]?.DeepClone(), p["min"]?.DeepClone(), p["max"]?.DeepClone(), p["step"]?.DeepClone(),
                values[(string)p["key"]!]?.DeepClone()))
            .ToList();
        return new StrategyDetailDto(
            Summary(strategy, version, run),
            rules,
            new VersionDto(version.Id, version.VersionNumber, version.State, version.CodeVersion, parameters),
            run is null ? null : new RunSummaryDto(run.Id, run.RunNumber, run.Status, run.PeriodStart, run.PeriodEnd, run.OosStart, run.FinishedAt, JsonNode.Parse(run.Metrics)!),
            (await signals.ActiveForStrategyAsync(strategy.Id, cancellationToken))
                .Select(x => new StrategySignalDto(x.Id, x.Symbol, x.Type, x.Score, x.Entry, x.EvaluatedAt)).ToList());
    }

    /// <summary>ST2 (SYSTEM_DESIGN.md §11.9): ids or codes, comma-separated.</summary>
    public async Task<Result<CompareDto>> CompareAsync(string? ids, CancellationToken cancellationToken)
    {
        var refs = (ids ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();
        if (refs.Count is 0 or > 5)
        {
            return BadCompare;
        }

        var strategies = new List<Strategy>();
        foreach (var r in refs)
        {
            if (await FindAsync(r, cancellationToken) is not { } found)
            {
                return NotFound;
            }

            strategies.Add(found);
        }

        var runs = await LatestRunsAsync(strategies.Select(x => x.Id).ToList(), cancellationToken);
        var series = new List<CompareSeriesDto>();
        var metrics = new List<CompareMetricsDto>();
        foreach (var strategy in strategies)
        {
            if (runs.GetValueOrDefault(strategy.Id) is not { } run)
            {
                continue;
            }

            var curve = JsonNode.Parse(run.EquityCurve)!.AsArray();
            var points = curve.Select(p => new ComparePointDto(
                DateOnly.Parse(p![0]!.GetValue<string>(), CultureInfo.InvariantCulture),
                Math.Round(decimal.Parse(p[1]!.GetValue<string>(), CultureInfo.InvariantCulture) / run.InitialCapital, 4, MidpointRounding.ToEven).ToString("0.0000", CultureInfo.InvariantCulture))).ToList();
            series.Add(new CompareSeriesDto(strategy.Id, strategy.Name, points));
            metrics.Add(new CompareMetricsDto(strategy.Id, strategy.Name, JsonNode.Parse(run.Metrics)?["full"]?.DeepClone()));
        }

        return new CompareDto(series, metrics);
    }

    /// <summary>ST4. <paramref name="runRef"/> is a run id or <c>latest</c>.</summary>
    public async Task<Result<BacktestRunDto>> RunAsync(string idOrCode, string runRef, CancellationToken cancellationToken)
    {
        if (await FindAsync(idOrCode, cancellationToken) is not { } strategy)
        {
            return NotFound;
        }

        BacktestRun? run;
        if (string.Equals(runRef, "latest", StringComparison.OrdinalIgnoreCase))
        {
            run = (await LatestRunsAsync([strategy.Id], cancellationToken)).GetValueOrDefault(strategy.Id);
        }
        else
        {
            run = Guid.TryParse(runRef, out var runId)
                ? await db.Runs.AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId && r.StrategyId == strategy.Id, cancellationToken)
                : null;
        }

        if (run is null)
        {
            return RunNotFound;
        }

        var version = await db.Versions.AsNoTracking().Where(v => v.Id == run.StrategyVersionId).Select(v => v.VersionNumber).SingleAsync(cancellationToken);
        return new BacktestRunDto(
            run.Id, run.StrategyId, run.RunNumber, version, run.Status, run.PeriodStart, run.PeriodEnd, run.OosStart, run.InitialCapital,
            JsonNode.Parse(run.Assumptions)!, JsonNode.Parse(run.Metrics)!, JsonNode.Parse(run.EquityCurve)!, JsonNode.Parse(run.DrawdownCurve)!, JsonNode.Parse(run.MonthlyReturns)!,
            run.FinishedAt, run.ErrorCode);
    }

    /// <summary>ST5: month-to-date results per strategy (illustrative until M5).</summary>
    public async Task<Result<IReadOnlyList<StrategyPeriodDto>>> SummaryAsync(string? period, CancellationToken cancellationToken)
    {
        if (period is not (null or "mtd"))
        {
            return UnsupportedPeriod;
        }

        var strategies = await SystemStrategies().OrderBy(s => s.DisplayOrder).ToListAsync(cancellationToken);
        return strategies
            .Select(s => (s, live: Live(s)))
            .Where(x => x.live is not null)
            .Select(x => new StrategyPeriodDto(x.s.Id, x.s.Name, x.live!.MtdPnl, x.live.MtdPct, x.live.MtdTrades, x.live.Illustrative))
            .ToList();
    }

    private static GuardDto? Guard(Strategy s) =>
        s is { GuardDrawdown: { } drawdown, GuardAsOf: { } asOf }
            ? new GuardDto(
                drawdown.ToString("0.0000", CultureInfo.InvariantCulture), DrawdownGuard.WarnAt(s.GuardPausePct).ToString("0.0000", CultureInfo.InvariantCulture),
                s.GuardPausePct.ToString("0.0000", CultureInfo.InvariantCulture), s.GuardWindowDays, asOf)
            : null;

    private IQueryable<Strategy> SystemStrategies() => db.Strategies.AsNoTracking().Where(s => s.OwnerUserId == null && s.Status != StrategyStatus.Retired);

    private async Task<Strategy?> FindAsync(string idOrCode, CancellationToken cancellationToken) =>
        Guid.TryParse(idOrCode, out var id)
            ? await SystemStrategies().SingleOrDefaultAsync(s => s.Id == id, cancellationToken)
            : await SystemStrategies().SingleOrDefaultAsync(s => s.Code == idOrCode, cancellationToken);

    private async Task<Dictionary<Guid, StrategyVersion>> PublishedVersionsAsync(IReadOnlyCollection<Strategy> strategies, CancellationToken cancellationToken)
    {
        var ids = strategies.Where(s => s.PublishedVersionId != null).Select(s => s.PublishedVersionId!.Value).ToList();
        return await db.Versions.AsNoTracking().Where(v => ids.Contains(v.Id)).ToDictionaryAsync(v => v.StrategyId, cancellationToken);
    }

    private async Task<Dictionary<Guid, BacktestRun>> LatestRunsAsync(IReadOnlyCollection<Guid> strategyIds, CancellationToken cancellationToken)
    {
        var runs = await db.Runs.AsNoTracking()
            .Where(r => strategyIds.Contains(r.StrategyId) && r.Status == RunStatus.Completed)
            .ToListAsync(cancellationToken);
        return runs.GroupBy(r => r.StrategyId).ToDictionary(g => g.Key, g => g.MaxBy(r => r.RunNumber)!);
    }

    private static StrategySummaryDto Summary(Strategy s, StrategyVersion? version, BacktestRun? run)
    {
        StrategyMetricsDto? metrics = null;
        IReadOnlyList<string> sparkline = [];
        if (run is not null)
        {
            var full = JsonNode.Parse(run.Metrics)!["full"]!;
            metrics = new StrategyMetricsDto((string)full["cagr"]!, (string)full["sharpe"]!, (string)full["maxDrawdown"]!, (string)full["winRate"]!);
            sparkline = Downsample(JsonNode.Parse(run.EquityCurve)!.AsArray().Select(p => (string)p![1]!).ToList(), SparklinePoints);
        }

        return new StrategySummaryDto(
            s.Id, s.Code, s.Name, s.Description, s.Status, s.DisplayOrder, version?.VersionNumber ?? 0, s.LiveSince, Live(s), metrics, sparkline, run?.PeriodStart, run?.PeriodEnd, Guard(s),
            BacktestIllustrative: run is null || JsonNode.Parse(run.Assumptions)?["illustrative"]?.GetValue<bool>() is not false); // seeded runs carry no flag
    }

    private static LiveResultsDto? Live(Strategy s)
    {
        if (s.IllustrativeLive is null)
        {
            return null;
        }

        var live = JsonNode.Parse(s.IllustrativeLive)!;
        return new LiveResultsDto((string)live["sinceInceptionPct"]!, (string)live["mtdPct"]!, (string)live["mtdPnl"]!, (int)live["mtdTrades"]!, Illustrative: true);
    }

    /// <summary>Evenly spaced points, always keeping the first and the last.</summary>
    private static IReadOnlyList<string> Downsample(IReadOnlyList<string> values, int points)
    {
        if (values.Count <= points)
        {
            return values;
        }

        return Enumerable.Range(0, points).Select(i => values[(int)Math.Round(i * (values.Count - 1) / (decimal)(points - 1), MidpointRounding.AwayFromZero)]).ToList();
    }
}
