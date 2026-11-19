namespace Astryx.Contracts.Paper;

/// <summary>
/// Owned by Paper: one broker sync for a user, now (SYSTEM_DESIGN.md §17.7, T-145), for pull-to-refresh. Settles the
/// user's in-flight orders and reconciles their connection; a no-op without one. Alpaca failures are swallowed: the
/// caller shows the last synced values and the connection's state.
/// </summary>
public interface IBrokerSync
{
    Task SyncUserAsync(Guid userId, CancellationToken cancellationToken);
}
