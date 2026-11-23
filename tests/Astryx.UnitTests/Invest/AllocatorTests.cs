using Astryx.Modules.Invest.Application;
using Astryx.Modules.Invest.Domain;

namespace Astryx.UnitTests.Invest;

/// <summary>The Invest track's arithmetic (TASKS.md T-164).</summary>
public class AllocatorTests
{
    private static readonly IReadOnlyDictionary<string, decimal> Prices = new Dictionary<string, decimal> { ["SPY"] = 600m, ["VXUS"] = 70m, ["BND"] = 73m };
    private static readonly IReadOnlyDictionary<string, decimal> Balanced = Mixes.Targets["balanced"];

    [Fact]
    public void Cash_is_spent_in_the_target_mix_in_fractional_shares_without_overdrawing()
    {
        var trades = Allocator.Invest(10_000m, Balanced, Prices);

        trades.Single(t => t.Symbol == "SPY").Quantity.ShouldBe(8.333333m); // $5,000 / 600, rounded down to 6 places
        trades.Single(t => t.Symbol == "VXUS").Quantity.ShouldBe(28.571428m); // $2,000 / 70
        trades.Single(t => t.Symbol == "BND").Quantity.ShouldBe(41.095890m); // $3,000 / 73
        trades.Sum(t => t.Quantity * t.Price).ShouldBeLessThanOrEqualTo(10_000m);
    }

    [Fact]
    public void Rebalancing_sells_what_is_over_target_first_and_ends_at_the_targets()
    {
        // All in stocks after a rally; the balanced mix wants 30 % in bonds.
        var holdings = new Dictionary<string, decimal> { ["SPY"] = 20m, ["VXUS"] = 0m, ["BND"] = 0m };

        var trades = Allocator.Rebalance(0m, holdings, Balanced, Prices);

        trades[0].ShouldSatisfyAllConditions(t => t.Symbol.ShouldBe("SPY"), t => t.Quantity.ShouldBe(-10m)); // $12,000 → $6,000
        var after = holdings.ToDictionary(h => h.Key, h => h.Value + trades.Where(t => t.Symbol == h.Key).Sum(t => t.Quantity));
        var (weights, drift) = Allocator.Drift(trades.Sum(t => -t.Quantity * t.Price), after, Balanced, Prices);
        drift.ShouldBeLessThan(0.001m);
        weights["BND"].ShouldBe(0.30m, 0.001m);
    }

    [Fact]
    public void Drift_measures_the_largest_distance_from_target()
    {
        var (weights, drift) = Allocator.Drift(0m, new Dictionary<string, decimal> { ["SPY"] = 10m, ["VXUS"] = 0m, ["BND"] = 0m }, Balanced, Prices);

        (weights["SPY"], drift).ShouldBe((1m, 0.50m)); // 100 % in a 50 % fund
    }

    [Theory]
    [InlineData("weekly", "2026-11-23", "2026-11-30")]
    [InlineData("monthly", "2026-01-31", "2026-02-28")]
    [InlineData("none", "2026-11-23", null)]
    public void Contribution_dates_follow_the_frequency(string frequency, string from, string? expected) =>
        Allocator.Next(frequency, DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture))
            .ShouldBe(expected is null ? null : DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
}
