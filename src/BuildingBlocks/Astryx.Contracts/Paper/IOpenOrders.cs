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
