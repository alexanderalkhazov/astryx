namespace Astryx.SharedKernel.Finance;

/// <summary>A non-negative share quantity (numeric(18,6)); whole shares in the MVP (Q-11).</summary>
public readonly record struct Quantity
{
    public Quantity(decimal value)
    {
        if (value < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Quantity cannot be negative.");
        }

        Value = value;
    }

    public decimal Value { get; }

    public static Quantity Zero => new(0m);

    public bool IsWhole => decimal.Truncate(Value) == Value;

    public bool IsZero => Value == 0m;

    /// <summary>Largest whole quantity not exceeding <paramref name="raw"/> (sizing always floors).</summary>
    public static Quantity FloorOf(decimal raw) => new(RoundingPolicy.FloorToWholeShares(Math.Max(raw, 0m)));

    public static Quantity Min(Quantity left, Quantity right) => left.Value <= right.Value ? left : right;

    public static bool operator >(Quantity left, Quantity right) => left.Value > right.Value;

    public static bool operator <(Quantity left, Quantity right) => left.Value < right.Value;

    public static bool operator >=(Quantity left, Quantity right) => left.Value >= right.Value;

    public static bool operator <=(Quantity left, Quantity right) => left.Value <= right.Value;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
