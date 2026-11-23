using System.Globalization;
using System.Text.Json.Nodes;
using Astryx.Contracts.Strategies;
using Astryx.Modules.Strategies.Domain;
using Astryx.Modules.Strategies.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Strategies.Application;

/// <summary>
/// <see cref="IStrategyEvidence"/> from each strategy's newest completed run that a real backtest produced (T-160):
/// seeded illustrative runs count as no evidence at all.
/// </summary>
public sealed class StrategyEvidenceSource(StrategiesDbContext db) : IStrategyEvidence
{
    public async Task<IReadOnlyDictionary<Guid, StrategyEvidence>> ByStrategyAsync(CancellationToken cancellationToken)
    {
        var strategies = await db.Strategies.AsNoTracking().Where(s => s.OwnerUserId == null).Select(s => new { s.Id, s.TrackRecord }).ToListAsync(cancellationToken);
        var ids = strategies.Select(s => s.Id).ToList();
        var runs = await db.Runs.AsNoTracking().Where(r => ids.Contains(r.StrategyId) && r.Status == RunStatus.Completed).ToListAsync(cancellationToken);
        var latest = runs.Where(IsReal).GroupBy(r => r.StrategyId).ToDictionary(g => g.Key, g => g.MaxBy(r => r.RunNumber)!);
        return strategies.ToDictionary(
            s => s.Id,
            s => (latest.TryGetValue(s.Id, out var run) ? Evidence(run) : StrategyEvidence.None) with { Soaked = TrackRecordJson.Read(s.TrackRecord)?.SoakMet == true });
    }

    public async Task<StrategyEvidence> ForVersionAsync(Guid strategyVersionId, CancellationToken cancellationToken)
    {
        var strategyId = await db.Versions.AsNoTracking().Where(v => v.Id == strategyVersionId).Select(v => (Guid?)v.StrategyId).SingleOrDefaultAsync(cancellationToken);
        return strategyId is { } id && (await ByStrategyAsync(cancellationToken)).TryGetValue(id, out var evidence) ? evidence : StrategyEvidence.None;
    }

    /// <summary>A real backtest records <c>"illustrative": false</c> in its assumptions; the seeded runs don't.</summary>
    public static bool IsReal(BacktestRun run) => JsonNode.Parse(run.Assumptions)?["illustrative"]?.GetValue<bool>() is false;

    public static StrategyEvidence Evidence(BacktestRun run)
    {
        var full = JsonNode.Parse(run.Metrics)?["full"];
        var cagr = Parse(full?["cagr"]);
        var multiple = Parse(full?["multiple"]);
        var benchmark = Parse(JsonNode.Parse(run.Assumptions)?["benchmark"]?["multiple"]);
        var verdict = cagr is null || multiple is null ? StrategyEvidence.Unproven
            : cagr <= 0m ? StrategyEvidence.Losing
            : benchmark is { } b && multiple < b ? StrategyEvidence.LagsMarket
            : StrategyEvidence.BeatsMarket;
        return new StrategyEvidence(verdict, cagr, multiple, benchmark, run.PeriodStart, run.PeriodEnd);
    }

    private static decimal? Parse(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var text) && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
}
