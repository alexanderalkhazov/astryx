namespace Astryx.Contracts.Paper;

/// <summary>
/// Whether a broker portfolio can take an entry in <c>Symbol</c> now (SYSTEM_DESIGN.md §17.3, §14.5, TASKS.md T-142).
/// <paramref name="Eligible"/> is null when the broker could not be asked (the asset lookup failed); the risk engine
/// then warns rather than fails, and Alpaca's own answer at submit time stays authoritative.
/// </summary>
public sealed record BrokerReadiness(bool Connected, bool CanPlaceOrders, bool? Eligible, string? Reason = null);

/// <summary>Owned by Paper; read by Trading (risk engine) through Contracts.</summary>
public interface IBrokerReadiness
{
    Task<BrokerReadiness> GetAsync(Guid connectionId, string symbol, CancellationToken cancellationToken);
}
