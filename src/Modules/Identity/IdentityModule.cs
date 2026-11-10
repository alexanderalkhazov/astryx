using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Identity.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Modules.Identity;

/// <summary>Composition entry point for the Identity module (SYSTEM_DESIGN.md §7).</summary>
public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration) =>
        services.AddModuleDbContext<IdentityDbContext>(IdentityDbContext.SchemaName, historyKey: "identity");
}
