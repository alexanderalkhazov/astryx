namespace Astryx.Contracts.Events.Identity;

/// <summary>A new account was created. Other modules provision their per-user defaults from it (SYSTEM_DESIGN.md §7.1).</summary>
public sealed record UserRegistered(Guid UserId, string? DisplayName) : IIntegrationEvent
{
    public static string EventName => "identity.user_registered.v1";
}
