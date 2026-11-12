using Astryx.Contracts.MarketData;
using Astryx.Modules.Quant.Application.Indicators;
using Microsoft.Extensions.Caching.Memory;

namespace Astryx.Modules.Quant.Application.Analysis;

/// <summary>
/// Universe relative-strength ranks (1–99) from final daily closes (SYSTEM_DESIGN.md §13.3). Computed for the whole
/// universe in one query and cached for 15 minutes; final bars change once a day.
/// </summary>
public sealed class RelativeStrengthRanks(IPriceHistoryProvider history, IMemoryCache cache)
{
    private static readonly int[] Offsets = [0, 63, 126, 189, 252];
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
    private const string CacheKey = "quant:rs-ranks";

    public async Task<int?> RankAsync(string symbol, CancellationToken cancellationToken)
    {
        var ranks = await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Lifetime;
            return await ComputeAsync(cancellationToken);
        });
        return ranks!.TryGetValue(symbol, out var rank) ? rank : null;
    }

    private async Task<IReadOnlyDictionary<string, int>> ComputeAsync(CancellationToken cancellationToken)
    {
        var ladder = await history.GetCloseLadderAsync(Offsets, cancellationToken);
        var scores = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var (symbol, closes) in ladder.Closes)
        {
            if (closes.All(c => c is > 0m))
            {
                // Same weights as RelativeStrength.Score: 0.4·ROC63 + 0.2·(ROC126 + ROC189 + ROC252).
                var last = closes[0]!.Value;
                scores[symbol] = 0.4m * (last / closes[1]!.Value - 1m) + 0.2m * (last / closes[2]!.Value - 1m)
                               + 0.2m * (last / closes[3]!.Value - 1m) + 0.2m * (last / closes[4]!.Value - 1m);
            }
        }

        return scores.Count == 0 ? new Dictionary<string, int>() : RelativeStrength.Rank(scores);
    }
}
