using System.Text.Json.Nodes;
using Astryx.Contracts.Quant;
using Astryx.Contracts.Strategies;
using Astryx.Modules.Strategies.Application.Rules;
using Astryx.Modules.Strategies.Domain;
using Astryx.Modules.Strategies.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Strategies.Application;

/// <summary>
/// <see cref="IStrategySignals"/>: every active or guarded system strategy whose rule is implemented, at its published
/// version. Paused and retired strategies produce no signals (§7.8). The strategy list is loaded once per scope, so a
/// scanner run reads it once for the whole universe.
/// </summary>
public sealed class StrategySignals(StrategiesDbContext db, IEnumerable<IStrategyRule> rules) : IStrategySignals
{
    private List<(Strategy Strategy, StrategyVersion Version, IStrategyRule Rule, JsonObject Params)>? _active;

    public async Task<IReadOnlyList<StrategyEvaluation>> EvaluateAsync(QuantFacts facts, CancellationToken cancellationToken)
    {
        _active ??= await LoadAsync(cancellationToken);
        return _active.Select(a =>
        {
            var result = a.Rule.Evaluate(facts, a.Params);
            var signal = result.Candidate is { } c
                ? new StrategySignal(c.SignalType, c.OrderType, c.Entry, c.Stop, c.Target, c.RewardRisk, c.Score, c.Breakdown, c.Reason)
                : null;
            return new StrategyEvaluation(a.Strategy.Id, a.Version.Id, a.Strategy.Code, a.Strategy.Name, a.Version.VersionNumber, signal, result.Miss?.Code);
        }).ToList();
    }

    private async Task<List<(Strategy, StrategyVersion, IStrategyRule, JsonObject)>> LoadAsync(CancellationToken cancellationToken)
    {
        var strategies = await db.Strategies.AsNoTracking()
            .Where(s => s.OwnerUserId == null && (s.Status == StrategyStatus.Active || s.Status == StrategyStatus.ActiveGuarded) && s.PublishedVersionId != null)
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);
        var versionIds = strategies.Select(s => s.PublishedVersionId!.Value).ToList();
        var versions = await db.Versions.AsNoTracking().Where(v => versionIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, cancellationToken);
        return strategies
            .Select(s => (s, version: versions[s.PublishedVersionId!.Value], rule: rules.FirstOrDefault(r => r.Code == s.Code)))
            .Where(x => x.rule is not null)
            .Select(x => (x.s, x.version, x.rule!, JsonNode.Parse(x.version.Params)!.AsObject()))
            .ToList();
    }
}
