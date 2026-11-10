using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Astryx.Infrastructure.Persistence;

/// <summary>
/// Applies every module's migrations in registration order (platform schema first). Run with <c>--migrate</c>
/// in production, or automatically when <c>Database:MigrateOnStartup</c> is true (Development) (SYSTEM_DESIGN.md §22).
/// </summary>
public static class MigrationRunner
{
    public static async Task MigrateAllAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(MigrationRunner));
        foreach (var registration in services.GetServices<ModuleDbContextRegistration>().OrderBy(r => r.Order).ThenBy(r => r.ContextType.Name, StringComparer.Ordinal))
        {
            await using var scope = services.CreateAsyncScope();
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(registration.ContextType);
            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            if (pending.Count == 0)
            {
                continue;
            }

            logger.LogInformation("Applying {Count} migration(s) for {Context} in schema {Schema}", pending.Count, registration.ContextType.Name, registration.Schema);
            await context.Database.MigrateAsync(cancellationToken);
        }
    }
}
