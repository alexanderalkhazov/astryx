using Astryx.Contracts.Events;
using Astryx.Contracts.Events.Identity;

namespace Astryx.Modules.Profiles.Application;

/// <summary>Creates the profile, default risk limits and notification preferences for a new user. Idempotent.</summary>
public sealed class ProvisionProfileHandler(ProfileProvisioner provisioner) : IIntegrationEventHandler<UserRegistered>
{
    public Task HandleAsync(UserRegistered integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken) =>
        provisioner.EnsureAsync(integrationEvent.UserId, integrationEvent.DisplayName, cancellationToken);
}
