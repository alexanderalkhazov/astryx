namespace Astryx.Contracts.Paper;

/// <summary>Working entry orders for the risk engine's <c>max_open_positions</c> and <c>no_duplicate</c> checks (§14.5).</summary>
public sealed record WorkingEntries(int Count, IReadOnlyList<Guid> InstrumentIds);

/// <summary>Owned by Paper; read by Trading through Contracts.</summary>
public interface IOpenOrders
{
    Task<WorkingEntries> GetWorkingEntriesAsync(Guid portfolioId, CancellationToken cancellationToken);
}
