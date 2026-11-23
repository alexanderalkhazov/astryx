using System.Text.Json.Nodes;
using Astryx.Contracts.MarketData;
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
public sealed partial class StrategySignals(StrategiesDbContext db, IEnumerable<IStrategyRule> rules, IStockResearchSource research, TimeProvider timeProvider) : IStrategySignals
{
    /// <summary>How far back an earnings headline may be for an Earnings Drift gap (the rule looks 3 sessions back).</summary>
    private static readonly TimeSpan EarningsNewsWindow = TimeSpan.FromDays(7L);

    private List<(Strategy Strategy, StrategyVersion Version, IStrategyRule Rule, JsonObject Params)>? _active;

    public async Task<IReadOnlyList<StrategyEvaluation>> EvaluateAsync(QuantFacts facts, CancellationToken cancellationToken)
    {
        _active ??= await LoadAsync(cancellationToken);
        var evaluations = new List<StrategyEvaluation>(_active.Count);
        foreach (var a in _active)
        {
            var result = a.Rule.Evaluate(facts, a.Params);
            if (result.Candidate is not null && a.Rule.Code == EarningsDriftRule.RuleCode && !await HasEarningsNewsAsync(facts.History.Symbol, cancellationToken))
            {
                result = RuleResult.None("no_earnings_news", "The gap has no earnings headline in the last week."); // rules-draft §4, option (b)
            }

            var signal = result.Candidate is { } c
                ? new StrategySignal(c.SignalType, c.OrderType, c.Entry, c.Stop, c.Target, c.RewardRisk, c.Score, c.Breakdown, c.Reason)
                : null;
            evaluations.Add(new StrategyEvaluation(a.Strategy.Id, a.Version.Id, a.Strategy.Code, a.Strategy.Name, a.Version.VersionNumber, signal, result.Miss?.Code));
        }

        return evaluations;
    }

    /// <summary>Alpaca has no earnings calendar, so a recent headline about results stands in for it (owner decision 2026-11-23).</summary>
    private async Task<bool> HasEarningsNewsAsync(string symbol, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var news = await research.GetNewsAsync(symbol, now - EarningsNewsWindow, now, 50, cancellationToken);
        return news.Any(n => EarningsHeadline().IsMatch(n.Headline));
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\b(earnings|quarterly|quarter|results|revenue|EPS|guidance|beats?|misses|Q[1-4])\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex EarningsHeadline();

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
