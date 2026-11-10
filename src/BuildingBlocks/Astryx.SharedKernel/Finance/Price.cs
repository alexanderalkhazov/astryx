namespace Astryx.SharedKernel.Finance;

/// <summary>A per-share price. Always strictly positive (SYSTEM_DESIGN.md §9.5, numeric(18,6)).</summary>
public readonly record struct Price
{
    public Price(decimal value)
    {
        if (value <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Price must be greater than zero.");
        }

        Value = value;
    }

    public decimal Value { get; }

    public static bool TryCreate(decimal value, out Price price)
    {
        price = value > 0m ? new Price(value) : default;
        return value > 0m;
    }

    public Price RoundToTick() => new(RoundingPolicy.PriceTick(Value));

    public static decimal operator -(Price left, Price right) => left.Value - right.Value;

    public static bool operator >(Price left, Price right) => left.Value > right.Value;

    public static bool operator <(Price left, Price right) => left.Value < right.Value;

    public static bool operator >=(Price left, Price right) => left.Value >= right.Value;

    public static bool operator <=(Price left, Price right) => left.Value <= right.Value;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
