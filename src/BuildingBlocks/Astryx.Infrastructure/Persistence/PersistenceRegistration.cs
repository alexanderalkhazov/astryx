using Astryx.Infrastructure.Health;
using Astryx.Infrastructure.Idempotency;
using Astryx.Infrastructure.Jobs;
using Astryx.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Astryx.Infrastructure.Persistence;

/// <summary>A module DbContext registered for migrations and the pending-migrations health check.</summary>
public sealed record ModuleDbContextRegistration(Type ContextType, string Schema, string HistoryTable, int Order);

public static class PersistenceRegistration
{
    /// <summary>Registers the shared data source, the platform schema and the migrations health check.</summary>
    public static IServiceCollection AddAstryxPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");
        }

        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddScoped<ModuleUnitOfWork>();
        services.AddModuleDbContext<InfraDbContext>(InfraDbContext.SchemaName, historyKey: "infra", order: 0);
        services.AddOutbox(configuration);
        services.AddJobQueue(configuration);
        services.AddIdempotency(configuration);
        services.AddScoped<Astryx.Infrastructure.Security.ICurrentUser, Astryx.Infrastructure.Security.CurrentUser>();
        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"])
            .AddCheck<PendingMigrationsHealthCheck>("migrations", tags: ["ready"]);
        return services;
    }

    /// <summary>
    /// Registers a module's context in its own schema with its own migrations history table
    /// (<c>{schema}.__ef_migrations_{historyKey}</c>), so modules sharing a schema migrate independently.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services, string schema, string historyKey, int order = 10)
        where TContext : ModuleDbContext
    {
        var historyTable = $"__ef_migrations_{historyKey}";
        services.AddDbContext<TContext>((provider, options) =>
        {
            // Inside an active unit of work every context shares its connection (see ModuleUnitOfWork).
            var unit = provider.GetService<ModuleUnitOfWork>();
            if (unit is { Active: true })
            {
                options.UseNpgsql(unit.Connection!, npgsql => npgsql.MigrationsHistoryTable(historyTable, schema));
            }
            else
            {
                options.UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>(), npgsql => npgsql.MigrationsHistoryTable(historyTable, schema));
            }

            options.UseSnakeCaseNamingConvention();
        });
        services.AddScoped(provider =>
        {
            var context = ActivatorUtilities.CreateInstance<TContext>(provider, provider.GetRequiredService<DbContextOptions<TContext>>());
            if (provider.GetService<ModuleUnitOfWork>() is { Active: true } unit)
            {
                context.Database.UseTransaction(unit.Transaction);
            }

            return context;
        });
        services.AddSingleton(new ModuleDbContextRegistration(typeof(TContext), schema, historyTable, order));
        return services;
    }
}
