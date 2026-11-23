using System.Text.Json;
using System.Text.Json.Nodes;
using Astryx.Contracts.Quant;
using Astryx.Infrastructure.Jobs;
using Astryx.Modules.Strategies.Application.Rules;
using Astryx.Modules.Strategies.Domain;
using Astryx.Modules.Strategies.Infrastructure.Persistence;
using Astryx.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Astryx.Modules.Strategies.Application.Backtesting;

public sealed record BacktestJob(string StrategyCode) : IJob
{
    public static string JobType => "strategies.backtest.v1";
}

/// <summary>A finished run's headline, for the dev endpoint and logs.</summary>
/// <param name="EqualWeightMultiple">Growth of $1 from holding every stock in the universe equally over the same period.</param>
public sealed record BacktestSummary(Guid RunId, string StrategyCode, int RunNumber, DateOnly PeriodStart, DateOnly PeriodEnd, int Trades, JsonNode Full, decimal? EqualWeightMultiple);

/// <summary>
/// Runs a strategy's published version through <see cref="Backtester"/> on the stored universe and records a completed
/// <c>backtest_runs</c> row with its trades (TASKS.md T-160). The Strategy lab shows the newest completed run, so this
/// replaces the seeded illustrative figures for that strategy.
/// </summary>
public sealed class BacktestService(StrategiesDbContext db, IBacktestData data, IEnumerable<IStrategyRule> rules, TimeProvider timeProvider, ILogger<BacktestService> logger)
    : IJobHandler<BacktestJob>
{
    /// <summary>About five years of sessions: one year warms up the relative-strength rank, the rest is tested.</summary>
    public const int MaxBars = 1300;

    public async Task HandleAsync(BacktestJob job, JobContext context, CancellationToken cancellationToken) => await RunAsync(job.StrategyCode, cancellationToken);

    /// <summary>Null when the strategy is unknown or has no implemented rule.</summary>
    public async Task<BacktestSummary?> RunAsync(string strategyCode, CancellationToken cancellationToken)
    {
        if (rules.FirstOrDefault(r => r.Code == strategyCode) is not { } rule
            || await db.Strategies.SingleOrDefaultAsync(s => s.Code == strategyCode && s.OwnerUserId == null, cancellationToken) is not { PublishedVersionId: { } versionId } strategy)
        {
            return null;
        }

        var version = await db.Versions.AsNoTracking().SingleAsync(v => v.Id == versionId, cancellationToken);
        var settings = new BacktestSettings();
        var started = timeProvider.GetUtcNow();
        var dataset = await data.LoadAsync(MaxBars, cancellationToken);
        logger.LogInformation("Backtest {Strategy}: loaded {Series} symbols over {Sessions} sessions in {Seconds:0.0} s", strategyCode, dataset.Series.Count, dataset.Sessions.Count, (timeProvider.GetUtcNow() - started).TotalSeconds);
        var result = Backtester.Run(dataset, rule, JsonNode.Parse(version.Params)!.AsObject(), settings);
        logger.LogInformation("Backtest {Strategy}: simulated {Trades} trades in {Seconds:0.0} s", strategyCode, result.Trades.Count, (timeProvider.GetUtcNow() - started).TotalSeconds);
        if (result.Equity.Count < 2)
        {
            logger.LogWarning("Backtest of {Strategy} skipped: not enough history for the relative-strength warm-up", strategyCode);
            return null;
        }

        var metrics = BacktestMetrics.Compute(result, settings.StartingCash);
        var benchmark = EqualWeightMultiple(dataset, result.Equity[0].Date, result.Equity[^1].Date);
        var runNumber = (await db.Runs.Where(r => r.StrategyId == strategy.Id).MaxAsync(r => (int?)r.RunNumber, cancellationToken) ?? 0) + 1;
        var run = new BacktestRun
        {
            Id = Ids.New(timeProvider),
            StrategyId = strategy.Id,
            StrategyVersionId = version.Id,
            RunNumber = runNumber,
            Status = RunStatus.Completed,
            PeriodStart = result.Equity[0].Date,
            PeriodEnd = result.Equity[^1].Date,
            OosStart = result.OutOfSampleStart,
            InitialCapital = settings.StartingCash,
            Assumptions = JsonSerializer.Serialize(new
            {
                illustrative = false,
                data = "Alpaca daily bars, split-adjusted (fake market in tests)",
                universe = $"Today's universe: {dataset.Series.Count} symbols",
                survivorship = "Uses today's universe: results are biased upward until point-in-time constituents are licensed.",
                fills = "Signals on each session's completed bar; orders work the next session (day orders). A bar touching stop and target counts as the stop.",
                sizing = $"Risk {settings.RiskPerTradePct:P2} of equity per trade, weight cap {settings.MaxPositionWeightPct:P0}, at most {settings.MaxOpenPositions} positions, cash only",
                benchmark = benchmark is { } multiple ? new { name = "holding every stock equally", multiple = multiple.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) } : null,
                slippageBps = settings.SlippageBps,
                fees = $"{settings.FeePerOrder:0.00} per order",
                codeVersion = rule.CodeVersion,
            }),
            Metrics = metrics.ToJsonString(),
            EquityCurve = BacktestMetrics.EquityCurve(result).ToJsonString(),
            DrawdownCurve = BacktestMetrics.DrawdownCurve(result).ToJsonString(),
            MonthlyReturns = BacktestMetrics.MonthlyReturns(result, settings.StartingCash).ToJsonString(),
            StartedAt = started,
            FinishedAt = timeProvider.GetUtcNow(),
        };
        db.Runs.Add(run);
        db.Trades.AddRange(result.Trades.Select(t => new BacktestTrade
        {
            Id = Ids.New(timeProvider),
            RunId = run.Id,
            InstrumentId = t.InstrumentId,
            Segment = t.EntryDate < result.OutOfSampleStart ? "is" : "oos",
            EntryDate = t.EntryDate,
            ExitDate = t.ExitDate,
            EntryPrice = t.EntryPrice,
            ExitPrice = t.ExitPrice,
            Quantity = t.Quantity,
            Pnl = t.Pnl,
            RMultiple = t.RMultiple,
            ExitReason = t.ExitReason,
        }));
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Backtest {Strategy} run {Run}: {Trades} trades {From}–{To}, x{Multiple} vs equal-weight x{Benchmark}", strategyCode, runNumber, result.Trades.Count, run.PeriodStart, run.PeriodEnd, metrics["full"]!["multiple"], benchmark);
        return new BacktestSummary(run.Id, strategyCode, runNumber, run.PeriodStart, run.PeriodEnd, result.Trades.Count, metrics["full"]!.DeepClone(), benchmark);
    }

    /// <summary>
    /// What simply holding the universe would have done over the same sessions: the average of each symbol's last close
    /// over its first close in the period (symbols trading on both ends). The honest yardstick for a trading strategy.
    /// </summary>
    public static decimal? EqualWeightMultiple(BacktestDataset dataset, DateOnly from, DateOnly to)
    {
        var multiples = dataset.Series
            .Select(s => (First: s.Bars.FirstOrDefault(b => b.SessionDate >= from), Last: s.Bars.LastOrDefault(b => b.SessionDate <= to)))
            .Where(x => x.First is { Close: > 0m } && x.Last is not null && x.Last.SessionDate > x.First.SessionDate)
            .Select(x => x.Last!.Close / x.First!.Close)
            .ToList();
        return multiples.Count == 0 ? null : multiples.Average();
    }
}
