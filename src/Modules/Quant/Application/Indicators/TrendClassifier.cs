using Astryx.Modules.Quant.Domain;

namespace Astryx.Modules.Quant.Application.Indicators;

/// <summary>
/// Trend (SYSTEM_DESIGN.md §13.3): <c>uptrend</c> if <c>C &gt; SMA50 &gt; SMA200</c> and SMA50 rose over the last
/// 10 bars, <c>downtrend</c> for the mirror image, otherwise <c>sideways</c>. With fewer than 200 bars the same rule
/// runs on SMA20/SMA50 and the result carries <see cref="Trend.ShortHistory"/>; below that it is insufficient.
/// </summary>
public static class TrendClassifier
{
    public const int MinBars = 200;
    public const int SlopeBars = 10;

    /// <summary>The short-history fallback needs SMA50 now and SMA20 ten bars ago.</summary>
    public const int FallbackMinBars = 50;

    public static Trend Classify(IReadOnlyList<decimal> closes, IIndicatorCalculator calculator)
    {
        if (closes.Count >= MinBars)
        {
            return new Trend(Rule(closes, calculator, fast: 50, slow: 200), ShortHistory: false);
        }

        return closes.Count >= FallbackMinBars
            ? new Trend(Rule(closes, calculator, fast: 20, slow: 50), ShortHistory: true)
            : new Trend(TrendState.Insufficient, ShortHistory: true);
    }

    /// <summary>
    /// <see cref="Classify"/> at every bar at once (T-160): the state at index <c>i</c> equals
    /// <c>Classify(closes[0..i])</c>, because the SMAs are causal.
    /// </summary>
    public static IReadOnlyList<TrendState> Series(IReadOnlyList<decimal> closes, IIndicatorCalculator calculator)
    {
        var states = new TrendState[closes.Count];
        if (closes.Count < FallbackMinBars)
        {
            Array.Fill(states, TrendState.Insufficient);
            return states;
        }

        var sma20 = calculator.Sma(closes, 20).Values;
        var sma50 = calculator.Sma(closes, 50).Values;
        var sma200 = closes.Count >= MinBars ? calculator.Sma(closes, 200).Values : null;
        for (var i = 0; i < closes.Count; i++)
        {
            var count = i + 1;
            states[i] = count >= MinBars ? At(closes[i], sma50[i]!.Value, sma200![i]!.Value, sma50[i]!.Value - sma50[i - SlopeBars]!.Value)
                : count >= FallbackMinBars ? At(closes[i], sma20[i]!.Value, sma50[i]!.Value, sma20[i]!.Value - sma20[i - SlopeBars]!.Value)
                : TrendState.Insufficient;
        }

        return states;
    }

    private static TrendState At(decimal close, decimal fastNow, decimal slowNow, decimal slope) =>
        close > fastNow && fastNow > slowNow && slope > 0m ? TrendState.Uptrend
        : close < fastNow && fastNow < slowNow && slope < 0m ? TrendState.Downtrend
        : TrendState.Sideways;

    private static TrendState Rule(IReadOnlyList<decimal> closes, IIndicatorCalculator calculator, int fast, int slow)
    {
        var fastSma = calculator.Sma(closes, fast).Values;
        var slowSma = calculator.Sma(closes, slow).Values;
        var last = closes.Count - 1;
        var close = closes[last];
        var fastNow = fastSma[last]!.Value;
        var slowNow = slowSma[last]!.Value;
        var slope = fastNow - fastSma[last - SlopeBars]!.Value;

        if (close > fastNow && fastNow > slowNow && slope > 0m)
        {
            return TrendState.Uptrend;
        }

        return close < fastNow && fastNow < slowNow && slope < 0m ? TrendState.Downtrend : TrendState.Sideways;
    }
}
