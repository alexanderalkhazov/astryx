using Astryx.Contracts.MarketData;

namespace Astryx.Contracts.Quant;

/// <summary>
/// What a strategy rule needs about one symbol (SYSTEM_DESIGN.md §13.4): the daily history and the indicators the
/// Quant engine computed from it. Null indicators mean too little history (never partial values).
/// </summary>
/// <param name="Trend"><c>uptrend</c>, <c>downtrend</c>, <c>sideways</c>, or null when insufficient.</param>
public sealed record QuantFacts(DailyHistory History, decimal? Atr14, decimal? RelVolume20, int? RelStrengthRank, string? Trend, decimal? Rsi14 = null);

/// <summary>Owned by Quant, used by Strategies through Contracts.</summary>
public interface IQuantFacts
{
    /// <summary>Facts for a symbol, or null when the symbol is unknown.</summary>
    Task<QuantFacts?> GetAsync(string symbol, CancellationToken cancellationToken);
}
