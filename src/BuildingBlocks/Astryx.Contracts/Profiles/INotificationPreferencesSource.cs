namespace Astryx.Contracts.Profiles;

/// <summary>Which notification categories a user wants pushed (S4); defaults when the user has no row.</summary>
public sealed record PushPreferences(bool Fills, bool Signals, bool StopProximity, bool StrategyGuard, bool System, bool DailySummary, bool PushEnabled);

/// <summary>Owned by Profiles; read by Notifications' <c>PushSender</c> through Contracts (SYSTEM_DESIGN.md §7.15).</summary>
public interface INotificationPreferencesSource
{
    Task<PushPreferences> GetAsync(Guid userId, CancellationToken cancellationToken);
}
