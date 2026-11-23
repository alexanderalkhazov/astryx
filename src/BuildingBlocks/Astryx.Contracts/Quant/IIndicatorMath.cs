namespace Astryx.Contracts.Quant;

/// <summary>
/// Owned by Quant: the §13.3 indicator formulas for strategy rules that need more than <see cref="QuantFacts"/> carries
/// (T-163). Each series is aligned to its input; values before a period has filled are null.
/// </summary>
public interface IIndicatorMath
{
    IReadOnlyList<decimal?> Sma(IReadOnlyList<decimal> values, int period);

    IReadOnlyList<decimal?> Ema(IReadOnlyList<decimal> values, int period);

    IReadOnlyList<decimal?> Rsi(IReadOnlyList<decimal> closes, int period);

    /// <summary>MACD 12/26/9: line, signal and histogram.</summary>
    (IReadOnlyList<decimal?> Line, IReadOnlyList<decimal?> Signal, IReadOnlyList<decimal?> Histogram) Macd(IReadOnlyList<decimal> closes);
}
