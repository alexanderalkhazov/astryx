using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Discovery.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Modules.Discovery;

/// <summary>Composition entry point for the Discovery module (SYSTEM_DESIGN.md §7).</summary>
public static class DiscoveryModule
{
    public static IServiceCollection AddDiscoveryModule(this IServiceCollection services, IConfiguration configuration) =>
        services.AddModuleDbContext<DiscoveryDbContext>(DiscoveryDbContext.SchemaName, historyKey: "discovery");
}
