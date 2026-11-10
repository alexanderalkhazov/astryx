using Astryx.Infrastructure.Health;
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
        services.AddModuleDbContext<InfraDbContext>(InfraDbContext.SchemaName, historyKey: "infra", order: 0);
        services.AddOutbox(configuration);
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
        services.AddDbContext<TContext>((provider, options) => options
            .UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>(), npgsql => npgsql.MigrationsHistoryTable(historyTable, schema))
            .UseSnakeCaseNamingConvention());
        services.AddSingleton(new ModuleDbContextRegistration(typeof(TContext), schema, historyTable, order));
        return services;
    }
}
