using System.Text.Json.Nodes;
using Astryx.Contracts.Quant;
using Astryx.Modules.Strategies.Application.Rules;
using Astryx.Modules.Strategies.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Strategies.Application;

/// <summary>Runs a strategy's published version against one symbol (the scanner's unit of work, T-065).</summary>
public sealed class StrategyEvaluator(StrategiesDbContext db, IQuantFacts quant, IEnumerable<IStrategyRule> rules)
{
    /// <summary>Null when the strategy has no implemented rule or the symbol is unknown.</summary>
    public async Task<RuleResult?> EvaluateAsync(string strategyCode, string symbol, CancellationToken cancellationToken)
    {
        if (rules.FirstOrDefault(r => r.Code == strategyCode) is not { } rule)
        {
            return null;
        }

        var parameters = await db.Strategies.AsNoTracking()
            .Where(s => s.Code == strategyCode)
            .Join(db.Versions.AsNoTracking(), s => s.PublishedVersionId, v => v.Id, (s, v) => v.Params)
            .SingleOrDefaultAsync(cancellationToken);
        if (parameters is null || await quant.GetAsync(symbol, cancellationToken) is not { } facts)
        {
            return null;
        }

        return rule.Evaluate(facts, JsonNode.Parse(parameters)!.AsObject());
    }
}
