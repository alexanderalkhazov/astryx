using Astryx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Astryx.Infrastructure.Health;

/// <summary>Not ready while any module has unapplied migrations (SYSTEM_DESIGN.md §23.3).</summary>
public sealed class PendingMigrationsHealthCheck(IServiceProvider services) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var pending = new Dictionary<string, object>();
        try
        {
            foreach (var registration in services.GetServices<ModuleDbContextRegistration>())
            {
                await using var scope = services.CreateAsyncScope();
                var db = (DbContext)scope.ServiceProvider.GetRequiredService(registration.ContextType);
                var names = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
                if (names.Count > 0)
                {
                    pending[registration.ContextType.Name] = names;
                }
            }
        }
        catch (Exception ex) when (ex is Npgsql.NpgsqlException or InvalidOperationException or TimeoutException)
        {
            return HealthCheckResult.Unhealthy("Could not read migration state", ex);
        }

        return pending.Count == 0
            ? HealthCheckResult.Healthy("All migrations applied")
            : HealthCheckResult.Unhealthy($"{pending.Count} context(s) have pending migrations", data: pending);
    }
}
