namespace Astryx.Modules.Strategies.Application.Backtesting;

/// <summary>
/// Square root, natural log and exponential in <see cref="decimal"/>, for annualised backtest metrics: modules may not
/// use floating point (BannedSymbols). Accurate to well under 1e-12 over the ranges metrics use.
/// </summary>
public static class DecimalMath
{
    private const decimal Ln2 = 0.6931471805599453094172321215m;

    public static decimal Sqrt(decimal x)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        if (x == 0m)
        {
            return 0m;
        }

        var guess = x > 1m ? x / 2m : 1m;
        for (var i = 0; i < 100; i++)
        {
            var next = (guess + x / guess) / 2m;
            if (Math.Abs(next - guess) <= 1e-24m)
            {
                return next;
            }

            guess = next;
        }

        return guess;
    }

    /// <summary>ln(x) for x &gt; 0: halve or double into [0.5, 2], then 2·atanh((x−1)/(x+1)).</summary>
    public static decimal Ln(decimal x)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(x);
        var k = 0;
        while (x > 2m)
        {
            x /= 2m;
            k++;
        }

        while (x < 0.5m)
        {
            x *= 2m;
            k--;
        }

        var y = (x - 1m) / (x + 1m);
        var y2 = y * y;
        var term = y;
        var sum = 0m;
        for (var n = 1; n < 400; n += 2)
        {
            var add = term / n;
            sum += add;
            if (Math.Abs(add) < 1e-26m)
            {
                break;
            }

            term *= y2;
        }

        return 2m * sum + k * Ln2;
    }

    /// <summary>e^x: split off whole multiples of ln 2, Taylor series for the rest.</summary>
    public static decimal Exp(decimal x)
    {
        var k = (int)Math.Round(x / Ln2, MidpointRounding.ToEven);
        var r = x - k * Ln2;
        var term = 1m;
        var sum = 1m;
        for (var n = 1; n < 60; n++)
        {
            term *= r / n;
            sum += term;
            if (Math.Abs(term) < 1e-26m)
            {
                break;
            }
        }

        return k >= 0 ? sum * Pow2(k) : sum / Pow2(-k);
    }

    /// <summary>x^y for x &gt; 0.</summary>
    public static decimal Pow(decimal x, decimal y) => Exp(y * Ln(x));

    private static decimal Pow2(int k)
    {
        var result = 1m;
        for (var i = 0; i < k; i++)
        {
            result *= 2m;
        }

        return result;
    }
}
