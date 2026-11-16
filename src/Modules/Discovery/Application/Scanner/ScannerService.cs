using System.Globalization;
using System.Text.Json;
using Astryx.Contracts.Events.Discovery;
using Astryx.Contracts.MarketData;
using Astryx.Contracts.Quant;
using Astryx.Contracts.Strategies;
using Astryx.Infrastructure.Observability;
using Astryx.Infrastructure.Outbox;
using Astryx.Modules.Discovery.Domain;
using Astryx.Modules.Discovery.Infrastructure.Persistence;
using Astryx.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astryx.Modules.Discovery.Application.Scanner;

/// <summary>
/// One scanner run (SYSTEM_DESIGN.md §7.4, §19.2): filter the universe, evaluate every active strategy on every
/// remaining symbol, store the signals, retire the previous ones and announce the new ones.
/// </summary>
public sealed class ScannerService(
    ScannerFaults faults,
    DiscoveryDbContext db,
    IInstrumentDirectory instruments,
    IQuantFacts quant,
    IStrategySignals strategies,
    IMarketDataFreshness freshness,
    IOptions<ScannerOptions> options,
    TimeProvider timeProvider,
    ILogger<ScannerService> logger)
{
    /// <summary>No symbol had usable data (empty, stale or missing history).</summary>
    public const string NoUsableData = "SCN-503";

    /// <summary>The run itself failed.</summary>
    public const string Unexpected = "SCN-500";

    /// <summary>Score weights of the default breakout scheme (§13.4), to store weight and value next to each contribution.</summary>
    private static readonly Dictionary<string, decimal> Weights = new() { ["setup"] = 30m, ["relativeStrength"] = 20m, ["volume"] = 20m, ["trend"] = 15m, ["rewardRisk"] = 15m };

    /// <summary>True when market data moved on since the last run (new quotes in session, new bars after the close).</summary>
    public async Task<bool> IsDueAsync(CancellationToken cancellationToken)
    {
        var now = await freshness.GetAsync(cancellationToken);
        if (now.QuotesAsOf is null && now.DailyBarsThrough is null)
        {
            return false;
        }

        var last = await db.ScannerRuns.AsNoTracking().Where(r => r.Status != ScannerRunStatus.Running).OrderByDescending(r => r.StartedAt).FirstOrDefaultAsync(cancellationToken);
        return last is null || now.DailyBarsThrough > last.BarsThrough || now.QuotesAsOf > last.QuotesAsOf;
    }

    public async Task<ScannerRun> RunAsync(string trigger, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var markers = await freshness.GetAsync(cancellationToken);
        var run = new ScannerRun
        {
            Id = Ids.New(timeProvider),
            Trigger = trigger,
            Status = ScannerRunStatus.Running,
            UniverseCode = settings.UniverseCode,
            QuotesAsOf = markers.QuotesAsOf,
            BarsThrough = markers.DailyBarsThrough,
            StartedAt = timeProvider.GetUtcNow(),
        };
        db.ScannerRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await ScanAsync(run, settings, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Scanner run {RunId} failed", run.Id);
            db.ChangeTracker.Clear();
            await db.ScannerRuns.Where(r => r.Id == run.Id).ExecuteUpdateAsync(
                s => s.SetProperty(r => r.Status, ScannerRunStatus.Failed).SetProperty(r => r.ErrorCode, Unexpected).SetProperty(r => r.FinishedAt, timeProvider.GetUtcNow()),
                cancellationToken);
            run.Status = ScannerRunStatus.Failed;
            run.ErrorCode = Unexpected;
        }

        AstryxTelemetry.ScannerRun(timeProvider.GetUtcNow() - run.StartedAt, run.Status);
        if (run.Status != ScannerRunStatus.Failed)
        {
            AstryxTelemetry.ScannerSucceeded(timeProvider.GetUtcNow());
        }

        logger.LogInformation(
            "Scanner run {RunId} {Status}: {Evaluated}/{Total} evaluated, {Skipped} skipped", run.Id, run.Status, run.SymbolsEvaluated, run.SymbolsTotal, run.SymbolsSkipped);
        return run;
    }

    private async Task ScanAsync(ScannerRun run, ScannerOptions settings, CancellationToken cancellationToken)
    {
        if (faults.Failing)
        {
            throw new InvalidOperationException("Injected scanner fault (/dev/faults).");
        }

        var universe = await instruments.ListActiveEquitiesAsync(cancellationToken);
        run.SymbolsTotal = universe.Count;
        var now = timeProvider.GetUtcNow();
        var found = new List<Signal>();
        DateTimeOffset? oldestBar = null;

        foreach (var instrument in universe)
        {
            QuantFacts? facts;
            try
            {
                facts = await quant.GetAsync(instrument.Symbol, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Scanner skipped {Symbol}", instrument.Symbol);
                run.SymbolsSkipped++;
                continue;
            }

            if (facts is null || facts.History.Bars.Count < 21 || facts.History.Stale || facts.History.LastBarAt is not { } lastBarAt)
            {
                run.SymbolsSkipped++;
                continue;
            }

            if (!PassesFilters(facts.History.Bars, settings))
            {
                continue; // outside the universe filters: neither evaluated nor a warning
            }

            run.SymbolsEvaluated++;
            oldestBar = oldestBar is null || lastBarAt < oldestBar ? lastBarAt : oldestBar;
            foreach (var evaluation in await strategies.EvaluateAsync(facts, cancellationToken))
            {
                if (evaluation.Signal is { } signal)
                {
                    found.Add(ToSignal(run, instrument, evaluation, signal, facts, lastBarAt, now, settings));
                }
            }
        }

        if (run.SymbolsEvaluated == 0)
        {
            run.Status = ScannerRunStatus.Failed;
            run.ErrorCode = NoUsableData;
            run.FinishedAt = timeProvider.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
            return; // previous signals stay as they were; the API reports the scanner as unavailable
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var previous = await db.Signals.Where(s => s.Status == SignalStatus.Active && s.ScannerRunId != run.Id).ToListAsync(cancellationToken);
        var renewed = found.Select(s => (s.InstrumentId, s.StrategyId)).ToHashSet();
        foreach (var old in previous)
        {
            old.Status = renewed.Contains((old.InstrumentId, old.StrategyId)) ? SignalStatus.Superseded : SignalStatus.Expired;
        }

        var wasActive = previous.Select(s => (s.InstrumentId, s.StrategyId)).ToHashSet();
        db.Signals.AddRange(found);
        foreach (var signal in found.Where(s => !wasActive.Contains((s.InstrumentId, s.StrategyId))))
        {
            db.Publish(new SignalDetected(signal.Id, signal.InstrumentId, signal.Symbol, signal.StrategyId, signal.StrategyName, signal.SignalType, signal.Score, signal.Reason, signal.EvaluatedAt));
        }

        await SyncUniverseAsync(settings.UniverseCode, universe, now, cancellationToken);
        run.DataAsOf = oldestBar;
        run.Status = run.SymbolsSkipped > 0 ? ScannerRunStatus.CompletedWithWarnings : ScannerRunStatus.Completed;
        run.FinishedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static bool PassesFilters(IReadOnlyList<DailyBar> bars, ScannerOptions settings)
    {
        var last = bars[^1].Close;
        var averageVolume = bars.Count > 20 ? bars.Skip(bars.Count - 21).Take(20).Average(b => (decimal)b.Volume) : bars.Average(b => (decimal)b.Volume);
        return last >= settings.MinPrice && averageVolume * last >= settings.MinAvgDollarVolume;
    }

    private Signal ToSignal(ScannerRun run, InstrumentSummary instrument, StrategyEvaluation evaluation, StrategySignal signal, QuantFacts facts, DateTimeOffset lastBarAt, DateTimeOffset now, ScannerOptions settings)
    {
        var breakdown = signal.Breakdown.Select(p =>
        {
            var weight = Weights.GetValueOrDefault(p.Key);
            return new { component = p.Key, weight = Dec(weight), value = weight == 0m ? null : Dec(Math.Round(p.Value / weight, 4)), contribution = Dec(p.Value) };
        });
        var metrics = new Dictionary<string, string?>
        {
            ["relVolume"] = facts.RelVolume20 is { } rv ? Fixed(rv, 2) : null,
            ["relStrength"] = facts.RelStrengthRank?.ToString(CultureInfo.InvariantCulture),
            ["rsi14"] = facts.Rsi14 is { } rsi ? Fixed(rsi, 1) : null,
            ["rewardRisk"] = Fixed(signal.RewardRisk, 2),
            ["atr14"] = facts.Atr14 is { } atr ? Fixed(atr, 4) : null,
        };
        return new Signal
        {
            Id = Ids.New(timeProvider),
            ScannerRunId = run.Id,
            InstrumentId = instrument.Id,
            Symbol = instrument.Symbol,
            StrategyVersionId = evaluation.StrategyVersionId,
            StrategyId = evaluation.StrategyId,
            StrategyName = evaluation.StrategyName,
            StrategyVersion = evaluation.Version,
            SignalType = signal.SignalType,
            Horizon = "swing",
            Score = signal.Score,
            ScoreBreakdown = JsonSerializer.Serialize(breakdown),
            Reason = signal.Reason,
            OrderType = signal.OrderType,
            EntryPrice = signal.Entry,
            StopPrice = signal.Stop,
            TargetPrice = signal.Target,
            Metrics = JsonSerializer.Serialize(metrics),
            Status = SignalStatus.Active,
            EvaluatedAt = now,
            DataAsOf = lastBarAt,
            ExpiresAt = now.AddHours(settings.SignalLifetimeHours),
        };
    }

    /// <summary>Records who joined and left the universe (§10.4 universe_members).</summary>
    private async Task SyncUniverseAsync(string code, IReadOnlyList<InstrumentSummary> universe, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var current = universe.Select(i => i.Id).ToHashSet();
        var members = await db.UniverseMembers.Where(m => m.UniverseCode == code && m.RemovedAt == null).ToListAsync(cancellationToken);
        foreach (var gone in members.Where(m => !current.Contains(m.InstrumentId)))
        {
            gone.RemovedAt = now;
        }

        var known = members.Select(m => m.InstrumentId).ToHashSet();
        var rejoining = await db.UniverseMembers.Where(m => m.UniverseCode == code && m.RemovedAt != null).ToDictionaryAsync(m => m.InstrumentId, cancellationToken);
        foreach (var id in current.Where(id => !known.Contains(id)))
        {
            if (rejoining.TryGetValue(id, out var back))
            {
                back.RemovedAt = null;
                back.AddedAt = now;
            }
            else
            {
                db.UniverseMembers.Add(new UniverseMember { UniverseCode = code, InstrumentId = id, AddedAt = now });
            }
        }
    }

    private static string Dec(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Presentation string with a fixed number of places, like the M7 values ("2.40").</summary>
    private static string Fixed(decimal value, int places) =>
        Math.Round(value, places, MidpointRounding.ToEven).ToString("F" + places.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
}
