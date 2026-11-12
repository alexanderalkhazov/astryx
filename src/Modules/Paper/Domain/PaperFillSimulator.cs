namespace Astryx.Modules.Paper.Domain;

/// <summary>A simulated execution: the price paid or received, what it was measured against, and the slippage taken.</summary>
public sealed record SimulatedFill(decimal Price, decimal ReferencePrice, decimal SlippagePerShare);

/// <summary>
/// Paper fill rules for entries on a quote tick (SYSTEM_DESIGN.md §15.2). Pure. A buy stop fills once the last price
/// reaches the stop, at max(stop, last) + slippage; below it the order works. A limit buy fills at min(limit, last)
/// when touched, with no adverse slippage (documented optimism). A market buy fills at last + slippage.
/// </summary>
public static class PaperFillSimulator
{
    public const string Model = "paper.v1";

    /// <summary>The fill now, or null when the entry should rest as a working order.</summary>
    public static SimulatedFill? EntryOnTick(string orderType, decimal entry, decimal last, decimal slippagePerShare) => orderType switch
    {
        "buy_stop" when last >= entry => new SimulatedFill(Math.Max(entry, last) + slippagePerShare, Math.Max(entry, last), slippagePerShare),
        "limit" when last <= entry => new SimulatedFill(Math.Min(entry, last), Math.Min(entry, last), 0m),
        "market" => new SimulatedFill(last + slippagePerShare, last, slippagePerShare),
        "buy_stop" or "limit" => null,
        _ => throw new ArgumentOutOfRangeException(nameof(orderType), orderType, "Unknown entry order type."),
    };

    /// <summary>Cash to hold back for a working entry: the worst fill plus the fee.</summary>
    public static decimal Reservation(string orderType, decimal quantity, decimal entry, decimal slippagePerShare, decimal fee) =>
        (quantity * (orderType == "limit" ? entry : entry + slippagePerShare)) + fee;
}
