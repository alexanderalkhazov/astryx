using Astryx.Modules.Quant.Domain;

namespace Astryx.Modules.Quant.Application.Indicators;

/// <summary>Deterministic indicators over decimals (SYSTEM_DESIGN.md §13.2–13.3).</summary>
public interface IIndicatorCalculator
{
    IndicatorSeries Sma(IReadOnlyList<decimal> values, int period);

    IndicatorSeries Ema(IReadOnlyList<decimal> values, int period);

    IndicatorSeries Rsi(IReadOnlyList<decimal> closes, int period = 14);

    MacdSeries Macd(IReadOnlyList<decimal> closes, int fast = 12, int slow = 26, int signal = 9);

    IndicatorSeries Atr(IReadOnlyList<Bar> bars, int period = 14);

    IndicatorSeries RelativeVolume(IReadOnlyList<Bar> bars, int period = 20);

    IndicatorSeries Momentum(IReadOnlyList<decimal> closes, int period = 20);

    IndicatorSeries Volatility(IReadOnlyList<decimal> closes, int period = 20);
}

/// <summary>
/// The formulas of §13.3, in full <c>decimal</c> precision; nothing is rounded here (rounding is for storage and
/// display). Too little history gives <see cref="IndicatorStatus.Insufficient"/> with all-null values.
/// </summary>
public sealed class IndicatorCalculator : IIndicatorCalculator
{
    /// <summary>Trading days per year for annualising volatility.</summary>
    public const int TradingDays = 252;

    public IndicatorSeries Sma(IReadOnlyList<decimal> values, int period)
    {
        Guard(period);
        if (values.Count < period)
        {
            return IndicatorSeries.Insufficient(values.Count, period);
        }

        var result = new decimal?[values.Count];
        var sum = 0m;
        for (var i = 0; i < values.Count; i++)
        {
            sum += values[i];
            if (i >= period)
            {
                sum -= values[i - period];
            }

            if (i >= period - 1)
            {
                result[i] = sum / period;
            }
        }

        return new IndicatorSeries(result, IndicatorStatus.Ok, period, period - 1);
    }

    public IndicatorSeries Ema(IReadOnlyList<decimal> values, int period)
    {
        Guard(period);
        if (values.Count < period)
        {
            return IndicatorSeries.Insufficient(values.Count, period);
        }

        return new IndicatorSeries(EmaValues(values, 0, period), IndicatorStatus.Ok, period, Math.Min(2 * period - 1, values.Count - 1));
    }

    public IndicatorSeries Rsi(IReadOnlyList<decimal> closes, int period = 14)
    {
        Guard(period);
        var minBars = period + 1;
        if (closes.Count < minBars)
        {
            return IndicatorSeries.Insufficient(closes.Count, minBars);
        }

        var result = new decimal?[closes.Count];
        decimal averageGain = 0m, averageLoss = 0m;
        for (var i = 1; i <= period; i++)
        {
            var delta = closes[i] - closes[i - 1];
            averageGain += Math.Max(delta, 0m);
            averageLoss += Math.Max(-delta, 0m);
        }

        averageGain /= period;
        averageLoss /= period;
        result[period] = RsiValue(averageGain, averageLoss);
        for (var i = period + 1; i < closes.Count; i++)
        {
            var delta = closes[i] - closes[i - 1];
            averageGain = (averageGain * (period - 1) + Math.Max(delta, 0m)) / period;
            averageLoss = (averageLoss * (period - 1) + Math.Max(-delta, 0m)) / period;
            result[i] = RsiValue(averageGain, averageLoss);
        }

        return new IndicatorSeries(result, IndicatorStatus.Ok, minBars, period);
    }

    public MacdSeries Macd(IReadOnlyList<decimal> closes, int fast = 12, int slow = 26, int signal = 9)
    {
        Guard(fast);
        Guard(signal);
        if (slow <= fast)
        {
            throw new ArgumentOutOfRangeException(nameof(slow), "The slow period must be longer than the fast one.");
        }

        var lineMin = slow;
        var minBars = slow + signal - 1;
        if (closes.Count < minBars)
        {
            return new MacdSeries(
                IndicatorSeries.Insufficient(closes.Count, lineMin), IndicatorSeries.Insufficient(closes.Count, minBars), IndicatorSeries.Insufficient(closes.Count, minBars));
        }

        var fastEma = EmaValues(closes, 0, fast);
        var slowEma = EmaValues(closes, 0, slow);
        var line = new decimal?[closes.Count];
        var lineValues = new List<decimal>(closes.Count);
        for (var i = slow - 1; i < closes.Count; i++)
        {
            line[i] = fastEma[i]!.Value - slowEma[i]!.Value;
            lineValues.Add(line[i]!.Value);
        }

        // Signal = EMA(signal) of the MACD line, seeded by the SMA of its first `signal` values.
        var signalOnLine = EmaValues(lineValues, 0, signal);
        var signalLine = new decimal?[closes.Count];
        var histogram = new decimal?[closes.Count];
        for (var k = signal - 1; k < lineValues.Count; k++)
        {
            var i = slow - 1 + k;
            signalLine[i] = signalOnLine[k];
            histogram[i] = line[i] - signalLine[i];
        }

        var reliable = Math.Min(2 * slow + signal - 2, closes.Count - 1);
        return new MacdSeries(
            new IndicatorSeries(line, IndicatorStatus.Ok, lineMin, Math.Min(2 * slow - 1, closes.Count - 1)),
            new IndicatorSeries(signalLine, IndicatorStatus.Ok, minBars, reliable),
            new IndicatorSeries(histogram, IndicatorStatus.Ok, minBars, reliable));
    }

    public IndicatorSeries Atr(IReadOnlyList<Bar> bars, int period = 14)
    {
        Guard(period);
        var minBars = period + 1;
        if (bars.Count < minBars)
        {
            return IndicatorSeries.Insufficient(bars.Count, minBars);
        }

        // TR needs the previous close, so the first true range is at index 1 and the first ATR at index n.
        var result = new decimal?[bars.Count];
        var atr = 0m;
        for (var i = 1; i <= period; i++)
        {
            atr += TrueRange(bars[i], bars[i - 1].Close);
        }

        atr /= period;
        result[period] = atr;
        for (var i = period + 1; i < bars.Count; i++)
        {
            atr = (atr * (period - 1) + TrueRange(bars[i], bars[i - 1].Close)) / period;
            result[i] = atr;
        }

        return new IndicatorSeries(result, IndicatorStatus.Ok, minBars, period);
    }

    public IndicatorSeries RelativeVolume(IReadOnlyList<Bar> bars, int period = 20)
    {
        Guard(period);
        var minBars = period + 1;
        if (bars.Count < minBars)
        {
            return IndicatorSeries.Insufficient(bars.Count, minBars);
        }

        var result = new decimal?[bars.Count];
        var window = 0m; // volume of the `period` bars before i, excluding bar i itself
        for (var i = 0; i < period; i++)
        {
            window += bars[i].Volume;
        }

        for (var i = period; i < bars.Count; i++)
        {
            var average = window / period;
            result[i] = average == 0m ? null : bars[i].Volume / average;
            window += bars[i].Volume - bars[i - period].Volume;
        }

        return new IndicatorSeries(result, IndicatorStatus.Ok, minBars, period);
    }

    public IndicatorSeries Momentum(IReadOnlyList<decimal> closes, int period = 20)
    {
        Guard(period);
        var minBars = period + 1;
        if (closes.Count < minBars)
        {
            return IndicatorSeries.Insufficient(closes.Count, minBars);
        }

        var result = new decimal?[closes.Count];
        for (var i = period; i < closes.Count; i++)
        {
            result[i] = closes[i - period] == 0m ? null : closes[i] / closes[i - period] - 1m;
        }

        return new IndicatorSeries(result, IndicatorStatus.Ok, minBars, period);
    }

    /// <summary>
    /// Annualised sample standard deviation of log returns. <c>Math.Log</c> needs <c>double</c>: this is the
    /// <b>only</b> double arithmetic in the quant engine (§13.3). The result goes back to decimal at 6 dp and is
    /// never used in money arithmetic.
    /// </summary>
    public IndicatorSeries Volatility(IReadOnlyList<decimal> closes, int period = 20)
    {
        if (period < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(period), "Volatility needs at least two returns.");
        }

        var minBars = period + 1;
        if (closes.Count < minBars)
        {
            return IndicatorSeries.Insufficient(closes.Count, minBars);
        }

        var result = new decimal?[closes.Count];
        for (var i = period; i < closes.Count; i++)
        {
            result[i] = AnnualisedLogReturnStdev(closes, i, period);
        }

        return new IndicatorSeries(result, IndicatorStatus.Ok, minBars, period);
    }

#pragma warning disable RS0030 // the documented log step (§13.3): Math.Log and Math.Sqrt need double
    /// <summary>Sample stdev of the <paramref name="period"/> log returns ending at <paramref name="end"/>, × √252, back to decimal at 6 dp.</summary>
    private static decimal AnnualisedLogReturnStdev(IReadOnlyList<decimal> closes, int end, int period)
    {
        var returns = new double[period]; // float-ok: log returns (§13.3)
        for (var j = 0; j < period; j++)
        {
            var i = end - period + 1 + j;
            returns[j] = Math.Log((double)closes[i] / (double)closes[i - 1]); // float-ok: log returns (§13.3)
        }

        var mean = returns.Average();
        var squares = returns.Sum(r => (r - mean) * (r - mean));
        var annualised = Math.Sqrt(squares / (period - 1)) * Math.Sqrt(TradingDays);
        return Math.Round((decimal)annualised, 6, MidpointRounding.ToEven);
    }
#pragma warning restore RS0030

    /// <summary>EMA values aligned to <paramref name="values"/> (from <paramref name="start"/>), seeded by the SMA of the first n.</summary>
    private static decimal?[] EmaValues(IReadOnlyList<decimal> values, int start, int period)
    {
        var result = new decimal?[values.Count];
        var k = 2m / (period + 1);
        var seed = 0m;
        for (var i = start; i < start + period; i++)
        {
            seed += values[i];
        }

        var ema = seed / period;
        result[start + period - 1] = ema;
        for (var i = start + period; i < values.Count; i++)
        {
            ema = values[i] * k + ema * (1m - k);
            result[i] = ema;
        }

        return result;
    }

    private static decimal RsiValue(decimal averageGain, decimal averageLoss) =>
        averageLoss == 0m ? (averageGain == 0m ? 50m : 100m) : 100m - 100m / (1m + averageGain / averageLoss);

    private static decimal TrueRange(Bar bar, decimal previousClose) =>
        Math.Max(bar.High - bar.Low, Math.Max(Math.Abs(bar.High - previousClose), Math.Abs(bar.Low - previousClose)));

    private static void Guard(int period)
    {
        if (period < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(period), "Periods are at least 1.");
        }
    }
}
