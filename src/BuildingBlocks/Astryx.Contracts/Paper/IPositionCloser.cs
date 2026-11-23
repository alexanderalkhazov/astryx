namespace Astryx.Contracts.Paper;

/// <summary>
/// Owned by Paper: closes a user's position on the system's behalf (T-163, a strategy's time or indicator exit), through
/// the same path as the user's own close (§15.3; at Alpaca for an Alpaca portfolio). <paramref name="idempotencyKey"/>
/// makes a retried exit a no-op. Returns false when the position isn't open, or the session or quote doesn't allow it yet.
/// </summary>
public interface IPositionCloser
{
    Task<bool> CloseAsync(Guid userId, Guid positionId, string idempotencyKey, string exitReason, CancellationToken cancellationToken);
}
