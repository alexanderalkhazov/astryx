namespace Astryx.Contracts.Events.Profiles;

/// <summary>A user's risk limits changed; open risk assessments for that user become stale (SYSTEM_DESIGN.md §7.2, §14.5).</summary>
public sealed record RiskSettingsChanged(Guid UserId, int Version) : IIntegrationEvent
{
    public static string EventName => "profiles.risk_settings_changed.v1";
}
