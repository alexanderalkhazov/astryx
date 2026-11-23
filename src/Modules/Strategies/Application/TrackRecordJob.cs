using System.Text.Json;
using System.Text.Json.Nodes;
using Astryx.Contracts.Discovery;
using Astryx.Contracts.MarketData;
using Astryx.Infrastructure.Jobs;
using Astryx.Modules.Strategies.Application.Rules;
using Astryx.Modules.Strategies.Domain;
using Astryx.Modules.Strategies.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Astryx.Modules.Strategies.Application;

public sealed record TrackRecordJob : IJob
{
    public static string JobType => "strategies.track-record.v1";
}

/// <summary>Reads and writes <see cref="Strategy.TrackRecord"/>.</summary>
public static class TrackRecordJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static TrackRecordDto? Read(string? json) => json is null ? null : JsonSerializer.Deserialize<TrackRecordDto>(json, Options);

    public static string Write(TrackRecordDto record) => JsonSerializer.Serialize(record, Options);
}

/// <summary>
/// Every few hours (T-166): rebuilds each system strategy's live track record from the signals it really emitted, and
/// whether it has soaked long enough, within its backtest's drawdown, to be offered to beginners.
/// </summary>
public sealed class TrackRecordHandler(
    StrategiesDbContext db, IEmittedSignalHistory history, IPriceHistoryProvider prices, IEnumerable<IStrategyRule> rules, TimeProvider timeProvider, ILogger<TrackRecordHandler> logger)
    : IJobHandler<TrackRecordJob>
{
    /// <summary>Bars fetched per symbol: about two years, plenty for a signal's life and the exit rule's window.</summary>
    public const int Bars = 520;

    public async Task HandleAsync(TrackRecordJob job, JobContext context, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var byCode = rules.ToDictionary(r => r.Code, StringComparer.Ordinal);
        var strategies = await db.Strategies.Where(s => s.OwnerUserId == null && s.Status != StrategyStatus.Retired).ToListAsync(cancellationToken);
        var versionIds = strategies.Where(s => s.PublishedVersionId != null).Select(s => s.PublishedVersionId!.Value).ToList();
        var parameters = await db.Versions.AsNoTracking().Where(v => versionIds.Contains(v.Id)).ToDictionaryAsync(v => v.StrategyId, v => v.Params, cancellationToken);
        var bars = new Dictionary<string, IReadOnlyList<DailyBar>>(StringComparer.Ordinal);

        foreach (var strategy in strategies)
        {
            var signals = await history.ForStrategyAsync(strategy.Id, cancellationToken);
            var rule = byCode.GetValueOrDefault(strategy.Code);
            var ruleParams = JsonNode.Parse(parameters.GetValueOrDefault(strategy.Id) ?? "{}")!.AsObject();
            var outcomes = new List<SignalOutcome>(signals.Count);
            foreach (var signal in signals)
            {
                if (!bars.TryGetValue(signal.Symbol, out var series))
                {
                    series = (await prices.GetDailyAsync(signal.Symbol, Bars, cancellationToken))?.Bars ?? [];
                    bars[signal.Symbol] = series;
                }

                outcomes.Add(SignalFollower.Follow(signal, series, rule as IStrategyExit, ruleParams));
            }

            var stats = SignalFollower.Aggregate(outcomes);
            var backtestDrawdown = await BacktestDrawdownAsync(strategy.Id, cancellationToken);
            var soak = SignalFollower.SoakFor(stats, backtestDrawdown, today);
            strategy.TrackRecord = TrackRecordJson.Write(new TrackRecordDto(
                stats.Since, stats.Through, stats.Signals, stats.Triggered, stats.Closed, stats.Open, stats.WinRate, stats.AverageR, stats.TotalR, stats.MaxDrawdownR,
                backtestDrawdown, soak.Met, soak.Note, now));
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Rebuilt the live track record of {Count} strategies", strategies.Count);
    }

    /// <summary>The deepest R drawdown of the latest real backtest's trades, in exit order; null without one.</summary>
    private async Task<decimal?> BacktestDrawdownAsync(Guid strategyId, CancellationToken cancellationToken)
    {
        var runs = await db.Runs.AsNoTracking().Where(r => r.StrategyId == strategyId && r.Status == RunStatus.Completed).ToListAsync(cancellationToken);
        if (runs.Where(StrategyEvidenceSource.IsReal).MaxBy(r => r.RunNumber) is not { } run)
        {
            return null;
        }

        var rs = await db.Trades.AsNoTracking().Where(t => t.RunId == run.Id).OrderBy(t => t.ExitDate).ThenBy(t => t.EntryDate).Select(t => t.RMultiple).ToListAsync(cancellationToken);
        return SignalFollower.MaxDrawdownR(rs);
    }
}
