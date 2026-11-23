using Astryx.Modules.Invest.Domain;

namespace Astryx.Modules.Invest.Application;

/// <summary>A trade the plan should make: positive quantity buys, negative sells.</summary>
public sealed record FundTrade(string Symbol, decimal Quantity, decimal Price);

/// <summary>
/// The plan's arithmetic (T-164), pure and decimal: spend cash in the target proportions, or rebalance holdings to the
/// targets. Fractional shares are rounded down to 6 places, so cash never goes negative.
/// </summary>
public static class Allocator
{
    public const int QuantityScale = 6;

    public static decimal Floor(decimal quantity) => Math.Floor(quantity * 1_000_000m) / 1_000_000m;

    /// <summary>Buys spending <paramref name="cash"/> across the funds by weight.</summary>
    public static IReadOnlyList<FundTrade> Invest(decimal cash, IReadOnlyDictionary<string, decimal> targets, IReadOnlyDictionary<string, decimal> prices) =>
        targets.Where(t => prices.GetValueOrDefault(t.Key) > 0m)
            .Select(t => new FundTrade(t.Key, Floor(cash * t.Value / prices[t.Key]), prices[t.Key]))
            .Where(t => t.Quantity > 0m)
            .ToList();

    /// <summary>
    /// Sells what is above target and buys what is below, so every fund ends at its target share of the plan's value
    /// (cash included). Sells come first in the list, so the buys are funded.
    /// </summary>
    public static IReadOnlyList<FundTrade> Rebalance(decimal cash, IReadOnlyDictionary<string, decimal> holdings, IReadOnlyDictionary<string, decimal> targets, IReadOnlyDictionary<string, decimal> prices)
    {
        var total = cash + holdings.Sum(h => h.Value * prices.GetValueOrDefault(h.Key));
        var trades = new List<FundTrade>();
        foreach (var (symbol, weight) in targets.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            if (prices.GetValueOrDefault(symbol) is not (> 0m and var price))
            {
                continue;
            }

            var held = holdings.GetValueOrDefault(symbol);
            var gap = total * weight - held * price;
            var quantity = gap < 0m ? -Math.Min(held, Floor(-gap / price)) : Floor(gap / price);
            if (quantity != 0m)
            {
                trades.Add(new FundTrade(symbol, quantity, price));
            }
        }

        var spend = trades.Where(t => t.Quantity > 0m).Sum(t => t.Quantity * t.Price);
        var available = cash + trades.Where(t => t.Quantity < 0m).Sum(t => -t.Quantity * t.Price);
        if (spend > available && spend > 0m)
        {
            var scale = available / spend; // rounding: buy a touch less rather than overdraw
            trades = trades.Select(t => t.Quantity > 0m ? t with { Quantity = Floor(t.Quantity * scale) } : t).ToList();
        }

        return trades.OrderBy(t => t.Quantity > 0m).ThenBy(t => t.Symbol, StringComparer.Ordinal).ToList();
    }

    /// <summary>Each fund's share of the plan value, and the largest distance from its target.</summary>
    public static (IReadOnlyDictionary<string, decimal> Weights, decimal MaxDrift) Drift(decimal cash, IReadOnlyDictionary<string, decimal> holdings, IReadOnlyDictionary<string, decimal> targets, IReadOnlyDictionary<string, decimal> prices)
    {
        var total = cash + holdings.Sum(h => h.Value * prices.GetValueOrDefault(h.Key));
        var weights = targets.Keys.ToDictionary(s => s, s => total == 0m ? 0m : holdings.GetValueOrDefault(s) * prices.GetValueOrDefault(s) / total);
        return (weights, targets.Count == 0 ? 0m : targets.Max(t => Math.Abs(weights[t.Key] - t.Value)));
    }

    /// <summary>The next contribution date after <paramref name="from"/>.</summary>
    public static DateOnly? Next(string frequency, DateOnly from) => frequency switch
    {
        Frequencies.Weekly => from.AddDays(7),
        Frequencies.Monthly => from.AddMonths(1),
        _ => null,
    };
}
