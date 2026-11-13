namespace Astryx.Contracts.Paper;

/// <summary>Working entry orders for the risk engine's <c>max_open_positions</c> and <c>no_duplicate</c> checks (§14.5).</summary>
public sealed record WorkingEntries(int Count, IReadOnlyList<Guid> InstrumentIds);

/// <summary>Owned by Paper; read by Trading through Contracts.</summary>
public interface IOpenOrders
{
    Task<WorkingEntries> GetWorkingEntriesAsync(Guid portfolioId, CancellationToken cancellationToken);

    /// <summary>Working entry orders with their trigger, for the attention list (§11.8 P9).</summary>
    Task<IReadOnlyList<WorkingOrder>> ListWorkingEntriesAsync(Guid portfolioId, CancellationToken cancellationToken);
}

public sealed record WorkingOrder(Guid OrderId, string Symbol, decimal Quantity, decimal Trigger, DateTimeOffset? ExpiresAt);

/// <summary>Owned by Paper: the broker-side id of an order (e.g. <c>SIM-4G3AC2</c>) for position facts.</summary>
public interface IOrderLookup
{
    Task<string?> BrokerOrderIdAsync(Guid orderId, CancellationToken cancellationToken);
}

/// <summary>What a position's fills add up to (§20.13 checks 2–3): quantities, values and fees, buys and sells.</summary>
public sealed record PositionFillTotals(Guid PositionId, decimal BoughtQuantity, decimal BoughtValue, decimal SoldQuantity, decimal SoldValue, decimal Fees);

/// <summary>Owned by Paper; read by Portfolio's nightly reconciliation through Contracts (SYSTEM_DESIGN.md §20.13).</summary>
public interface IPaperReconciliationFacts
{
    Task<IReadOnlyList<PositionFillTotals>> FillTotalsAsync(Guid portfolioId, CancellationToken cancellationToken);

    /// <summary>Working orders whose expiry passed before <paramref name="before"/> (check 4).</summary>
    Task<IReadOnlyList<Guid>> WorkingPastExpiryAsync(Guid portfolioId, DateTimeOffset before, CancellationToken cancellationToken);

    /// <summary>Working OCO exit legs per position (check 5: an open position needs its stop and its target).</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlySet<string>>> WorkingExitsAsync(Guid portfolioId, CancellationToken cancellationToken);
}
