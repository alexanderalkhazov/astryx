using Astryx.Contracts.Events.Identity;
using Astryx.Infrastructure.Outbox;
using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Profiles.Application;
using Astryx.Modules.Profiles.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Modules.Profiles;

/// <summary>Composition entry point for the Profiles module (SYSTEM_DESIGN.md §7.2).</summary>
public static class ProfilesModule
{
    public static IServiceCollection AddProfilesModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<ProfilesDbContext>(ProfilesDbContext.SchemaName, historyKey: "profiles");
        services.AddIntegrationEventHandler<UserRegistered, ProvisionProfileHandler>();
        return services;
    }
}
