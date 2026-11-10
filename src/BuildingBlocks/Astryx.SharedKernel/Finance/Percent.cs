namespace Astryx.SharedKernel.Finance;

/// <summary>A ratio stored as a fraction: 0.0075 means 0.75% (SYSTEM_DESIGN.md §10.1, numeric(9,6)).</summary>
public readonly record struct Percent(decimal Fraction)
{
    public static Percent FromPercentage(decimal percentage) => new(percentage / 100m);

    public decimal AsPercentage => Fraction * 100m;

    public Money Of(Money amount) => amount * Fraction;

    public static bool operator >(Percent left, Percent right) => left.Fraction > right.Fraction;

    public static bool operator <(Percent left, Percent right) => left.Fraction < right.Fraction;

    public static bool operator >=(Percent left, Percent right) => left.Fraction >= right.Fraction;

    public static bool operator <=(Percent left, Percent right) => left.Fraction <= right.Fraction;

    public override string ToString() => Fraction.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
