using Astryx.Contracts.Audit;
using Astryx.Infrastructure.Jobs;
using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Audit.Application;
using Astryx.Modules.Audit.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Modules.Audit;

/// <summary>Composition entry point for the Audit module (SYSTEM_DESIGN.md §7.18).</summary>
public static class AuditModule
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<AuditDbContext>(AuditDbContext.SchemaName, historyKey: "audit");
        services.AddScoped<IAuditLog, AuditLogWriter>();
        services.AddJobHandler<AuditPartitionMaintenanceJob, AuditPartitionMaintenanceHandler>();
        services.AddRecurringJob("audit-partition-maintenance", TimeSpan.FromDays(1), _ => new AuditPartitionMaintenanceJob());
        return services;
    }
}
