using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Audit.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Modules.Audit;

/// <summary>Composition entry point for the Audit module (SYSTEM_DESIGN.md §7).</summary>
public static class AuditModule
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services, IConfiguration configuration) =>
        services.AddModuleDbContext<AuditDbContext>(AuditDbContext.SchemaName, historyKey: "audit");
}
