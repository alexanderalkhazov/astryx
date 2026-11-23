namespace Astryx.Contracts.Strategies;

/// <summary>
/// What a strategy's latest real backtest says (TASKS.md T-166): <c>unproven</c> (no real backtest yet),
/// <c>losing</c> (it lost money), <c>lags_market</c> (made money, less than holding every stock equally) or
/// <c>beats_market</c>. Figures are fractions; null when unproven.
/// </summary>
public sealed record StrategyEvidence(string Verdict, decimal? Cagr, decimal? Multiple, decimal? BenchmarkMultiple, DateOnly? PeriodStart, DateOnly? PeriodEnd)
{
    public const string Unproven = "unproven";
    public const string Losing = "losing";
    public const string LagsMarket = "lags_market";
    public const string BeatsMarket = "beats_market";

    public static readonly StrategyEvidence None = new(Unproven, null, null, null, null, null);
}

/// <summary>Owned by Strategies; read by Discovery (signals) and Trading (the risk engine's evidence check).</summary>
public interface IStrategyEvidence
{
    /// <summary>Evidence per strategy id, for every system strategy.</summary>
    Task<IReadOnlyDictionary<Guid, StrategyEvidence>> ByStrategyAsync(CancellationToken cancellationToken);

    /// <summary>The evidence of the strategy a version belongs to; <see cref="StrategyEvidence.None"/> when unknown.</summary>
    Task<StrategyEvidence> ForVersionAsync(Guid strategyVersionId, CancellationToken cancellationToken);
}
