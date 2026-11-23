using Astryx.Infrastructure.Security;

namespace Astryx.Modules.Invest.Domain;

/// <summary>
/// Row in <c>invest.plans</c> (TASKS.md T-164): a user's long-term paper portfolio of broad index funds, separate from
/// the trading account so it never touches the trading risk limits or an Alpaca connection.
/// </summary>
public sealed class InvestPlan : IUserOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary><c>cautious</c>, <c>balanced</c> or <c>growth</c>: picks the target mix (<see cref="Mixes"/>).</summary>
    public required string Profile { get; set; }

    public decimal Cash { get; set; }

    /// <summary>Everything deposited so far: the initial amount plus every contribution.</summary>
    public decimal Contributed { get; set; }

    public decimal ContributionAmount { get; set; }

    /// <summary><c>none</c>, <c>weekly</c> or <c>monthly</c>.</summary>
    public string ContributionFrequency { get; set; } = Frequencies.None;

    public DateOnly? NextContributionOn { get; set; }

    public decimal? GoalAmount { get; set; }

    public DateOnly? GoalDate { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public int Version { get; set; } = 1;
}

/// <summary>Row in <c>invest.holdings</c>: fractional shares of one fund.</summary>
public sealed class Holding : IUserOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid PlanId { get; set; }

    public required string Symbol { get; set; }

    public decimal Quantity { get; set; }

    public decimal CostBasis { get; set; }
}

/// <summary>Row in <c>invest.transactions</c>: deposits, buys and sells, append-only.</summary>
public sealed class InvestTransaction : IUserOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid PlanId { get; set; }

    /// <summary><c>deposit</c>, <c>buy</c> or <c>sell</c>.</summary>
    public required string Kind { get; set; }

    public string? Symbol { get; set; }

    public decimal? Quantity { get; set; }

    public decimal? Price { get; set; }

    /// <summary>Cash effect: positive for a deposit or sale, negative for a purchase.</summary>
    public decimal Amount { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}

public static class Frequencies
{
    public const string None = "none";
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { None, Weekly, Monthly };
}

/// <summary>A broad index fund the plan holds.</summary>
public sealed record Fund(string Symbol, string Name, string Role);

/// <summary>
/// The three-fund portfolio and the mix per profile (ASSUMPTION: conventional stock/bond splits for a cautious,
/// balanced and growth investor; not personal advice).
/// </summary>
public static class Mixes
{
    public static readonly IReadOnlyList<Fund> Funds =
    [
        new("SPY", "S&P 500 (US stocks)", "US stocks"),
        new("VXUS", "International stocks", "International stocks"),
        new("BND", "US bonds", "Bonds"),
    ];

    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, decimal>> Targets = new Dictionary<string, IReadOnlyDictionary<string, decimal>>
    {
        ["cautious"] = new Dictionary<string, decimal> { ["SPY"] = 0.30m, ["VXUS"] = 0.10m, ["BND"] = 0.60m },
        ["balanced"] = new Dictionary<string, decimal> { ["SPY"] = 0.50m, ["VXUS"] = 0.20m, ["BND"] = 0.30m },
        ["growth"] = new Dictionary<string, decimal> { ["SPY"] = 0.65m, ["VXUS"] = 0.25m, ["BND"] = 0.10m },
    };

    /// <summary>Rebalancing is suggested once any fund is this far from its target share.</summary>
    public const decimal RebalanceDrift = 0.05m;
}
