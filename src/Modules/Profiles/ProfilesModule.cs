using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Modules.Profiles;

/// <summary>Composition entry point for the Profiles module (SYSTEM_DESIGN.md §7). NOT IMPLEMENTED.</summary>
public static class ProfilesModule
{
    public static IServiceCollection AddProfilesModule(this IServiceCollection services, IConfiguration configuration) => services;
}
