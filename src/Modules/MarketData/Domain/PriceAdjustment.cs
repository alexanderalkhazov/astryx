using Astryx.Contracts.MarketData;

namespace Astryx.Modules.MarketData.Domain;

/// <summary>One action's effect on everything before its ex-date: prices multiply by <paramref name="Price"/>, volume by <paramref name="Volume"/>.</summary>
public sealed record AdjustmentFactor(DateOnly ExDate, decimal Price, decimal Volume);

/// <summary>
/// Back-adjustment of unadjusted bars (SYSTEM_DESIGN.md §20.7). A split of ratio r divides earlier prices by r and
/// multiplies earlier volume by r (a 1:10 reverse split has r = 0.1). In <see cref="AdjustmentMode.TotalReturn"/>, a
/// cash dividend D multiplies earlier prices by 1 − D / C, with C the unadjusted close of the session before the
/// ex-date. A bar is adjusted by the product of every factor whose ex-date is after it. Pure.
/// </summary>
public static class PriceAdjustment
{
    public static IReadOnlyList<AdjustmentFactor> Factors(IEnumerable<CorporateAction> actions, AdjustmentMode mode, Func<DateOnly, decimal?> closeBefore)
    {
        var factors = new List<AdjustmentFactor>();
        foreach (var action in actions)
        {
            switch (action.ActionType)
            {
                case CorporateActionType.Split or CorporateActionType.ReverseSplit when action.Ratio is > 0 and { } ratio:
                    factors.Add(new AdjustmentFactor(action.ExDate, 1m / ratio, ratio));
                    break;
                case CorporateActionType.CashDividend when mode == AdjustmentMode.TotalReturn && action.CashAmount is > 0 and { } cash:
                    if (closeBefore(action.ExDate) is { } close && close > cash)
                    {
                        factors.Add(new AdjustmentFactor(action.ExDate, 1m - (cash / close), 1m));
                    }

                    break;
            }
        }

        return factors;
    }

    /// <summary>The combined factor for a bar on <paramref name="session"/>.</summary>
    public static (decimal Price, decimal Volume) At(DateOnly session, IReadOnlyList<AdjustmentFactor> factors)
    {
        var price = 1m;
        var volume = 1m;
        foreach (var f in factors)
        {
            if (f.ExDate > session)
            {
                price *= f.Price;
                volume *= f.Volume;
            }
        }

        return (price, volume);
    }
}
