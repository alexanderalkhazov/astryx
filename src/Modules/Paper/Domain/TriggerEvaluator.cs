namespace Astryx.Modules.Paper.Domain;

/// <summary>
/// Prices seen since the last evaluation. A quote tick has no open (high = low = last); a bar (the EOD gap
/// reconciler) has its open, high and low, so gaps through a level fill at the open.
/// </summary>
public sealed record PriceWindow(decimal? Open, decimal High, decimal Low, decimal Last)
{
    public static PriceWindow Tick(decimal last) => new(null, last, last, last);
}

/// <summary>An exit trigger: which OCO leg fills, at what price, and whether a gap took it past the stop.</summary>
public sealed record ExitDecision(string Purpose, decimal Price, decimal ReferencePrice, decimal SlippagePerShare, bool GappedThroughStop);

/// <summary>
/// Trigger rules for working orders (SYSTEM_DESIGN.md §15.2). Pure. When one window touches both the stop and the
/// target, the stop is assumed to have filled first (the conservative choice, PAPER-05).
/// </summary>
public static class TriggerEvaluator
{
    public static SimulatedFill? Entry(string orderType, decimal entry, PriceWindow w, decimal slippagePerShare)
    {
        switch (orderType)
        {
            case "buy_stop" when w.High >= entry:
                {
                    var reference = w.Open is { } open ? Math.Max(open, entry) : Math.Max(entry, w.Last);
                    return new SimulatedFill(reference + slippagePerShare, reference, slippagePerShare);
                }

            case "limit" when w.Low <= entry:
                {
                    var reference = w.Open is { } open ? Math.Min(open, entry) : Math.Min(entry, w.Last);
                    return new SimulatedFill(reference, reference, 0m);
                }

            default:
                return null;
        }
    }

    public static ExitDecision? Exit(decimal stop, decimal target, PriceWindow w, decimal slippagePerShare)
    {
        if (w.Low <= stop)
        {
            var reference = w.Open is { } open ? Math.Min(open, stop) : Math.Min(stop, w.Last);
            return new ExitDecision(OrderPurpose.ExitStop, reference - slippagePerShare, reference, slippagePerShare, reference < stop);
        }

        return w.High >= target ? new ExitDecision(OrderPurpose.ExitTarget, target, target, 0m, false) : null;
    }

    /// <summary>Stop proximity (§15.4): the last price is above the stop by at most 1.5% of itself.</summary>
    public static bool NearStop(decimal last, decimal stop) => last > stop && (last - stop) / last <= 0.015m;
}
