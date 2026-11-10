namespace Astryx.SharedKernel.Finance;

/// <summary>An amount in a currency. Arithmetic across currencies throws (SYSTEM_DESIGN.md §9.5).</summary>
public readonly record struct Money(decimal Amount, Currency Currency)
{
    public static Money Zero(Currency currency) => new(0m, currency);

    public static Money Usd(decimal amount) => new(amount, Currency.USD);

    /// <summary>Notional value of <paramref name="quantity"/> at <paramref name="price"/>, unrounded.</summary>
    public static Money Of(Price price, Quantity quantity, Currency currency) => new(price.Value * quantity.Value, currency);

    public bool IsNegative => Amount < 0m;

    public bool IsZero => Amount == 0m;

    public Money RoundForLedger() => this with { Amount = RoundingPolicy.Ledger(Amount) };

    public static Money operator +(Money left, Money right) => new(left.Amount + right.Amount, Same(left, right));

    public static Money operator -(Money left, Money right) => new(left.Amount - right.Amount, Same(left, right));

    public static Money operator -(Money value) => value with { Amount = -value.Amount };

    public static Money operator *(Money value, decimal factor) => value with { Amount = value.Amount * factor };

    public static bool operator >(Money left, Money right) => Compare(left, right) > 0;

    public static bool operator <(Money left, Money right) => Compare(left, right) < 0;

    public static bool operator >=(Money left, Money right) => Compare(left, right) >= 0;

    public static bool operator <=(Money left, Money right) => Compare(left, right) <= 0;

    public override string ToString() => $"{Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)} {Currency}";

    private static int Compare(Money left, Money right)
    {
        _ = Same(left, right);
        return left.Amount.CompareTo(right.Amount);
    }

    private static Currency Same(Money left, Money right) =>
        left.Currency == right.Currency ? left.Currency : throw new CurrencyMismatchException(left.Currency, right.Currency);
}
