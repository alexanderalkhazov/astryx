using Astryx.Contracts.Quant;

namespace Astryx.Modules.Quant.Application.Indicators;

/// <summary><see cref="IIndicatorMath"/> over the same <see cref="IIndicatorCalculator"/> the facts and snapshots use.</summary>
public sealed class IndicatorMath(IIndicatorCalculator calculator) : IIndicatorMath
{
    public IReadOnlyList<decimal?> Sma(IReadOnlyList<decimal> values, int period) => Aligned(calculator.Sma(values, period).Values, values.Count);

    public IReadOnlyList<decimal?> Ema(IReadOnlyList<decimal> values, int period) => Aligned(calculator.Ema(values, period).Values, values.Count);

    public IReadOnlyList<decimal?> Rsi(IReadOnlyList<decimal> closes, int period) => Aligned(calculator.Rsi(closes, period).Values, closes.Count);

    public (IReadOnlyList<decimal?> Line, IReadOnlyList<decimal?> Signal, IReadOnlyList<decimal?> Histogram) Macd(IReadOnlyList<decimal> closes)
    {
        var macd = calculator.Macd(closes);
        return (Aligned(macd.Macd.Values, closes.Count), Aligned(macd.Signal.Values, closes.Count), Aligned(macd.Histogram.Values, closes.Count));
    }

    /// <summary>An insufficient series comes back empty; rules index by bar, so pad it with nulls.</summary>
    private static IReadOnlyList<decimal?> Aligned(IReadOnlyList<decimal?> values, int count) => values.Count == count ? values : new decimal?[count];
}
