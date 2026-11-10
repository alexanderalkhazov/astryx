namespace Astryx.SharedKernel.Finance;

/// <summary>
/// The only places where financial values are rounded (SYSTEM_DESIGN.md §20.1).
/// Everything else is computed at full decimal precision.
/// </summary>
public static class RoundingPolicy
{
    /// <summary>Ledger postings and displayed money: half-even to cents.</summary>
    public static decimal Ledger(decimal amount) => Math.Round(amount, 2, MidpointRounding.ToEven);

    /// <summary>Price tick: $0.01 at or above $1, $0.0001 below $1, half-even.</summary>
    public static decimal PriceTick(decimal price) =>
        Math.Round(price, TickDecimals(price), MidpointRounding.ToEven);

    /// <summary>
    /// Round an amount up to the tick of the instrument's <paramref name="price"/>, used for adverse slippage
    /// (1 bp of 186.50 = 0.01865 → 0.02). The tick depends on the price, not on the size of the amount.
    /// </summary>
    public static decimal CeilingToTick(decimal amount, decimal price)
    {
        var factor = TickFactor(price);
        return Math.Ceiling(amount * factor) / factor;
    }

    /// <summary>Whole shares only while fractional quantities are an open question (Q-11).</summary>
    public static decimal FloorToWholeShares(decimal quantity) => Math.Floor(quantity);

    private static int TickDecimals(decimal price) => Math.Abs(price) >= 1m ? 2 : 4;

    private static decimal TickFactor(decimal value) => Math.Abs(value) >= 1m ? 100m : 10_000m;
}
