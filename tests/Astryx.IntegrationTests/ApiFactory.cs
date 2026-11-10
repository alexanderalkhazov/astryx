using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace Astryx.IntegrationTests;

/// <summary>Boots the real API against a throwaway PostgreSQL container (SYSTEM_DESIGN.md §21.1).</summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    /// <summary>Whether the API applies migrations on startup (the Development default).</summary>
    protected virtual bool MigrateOnStartup => true;

    public string ConnectionString => _postgres.GetConnectionString();

    public Task InitializeAsync() => _postgres.StartAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", MigrateOnStartup ? "true" : "false");
        builder.UseSetting("Ai:Ollama:BaseUrl", "http://127.0.0.1:9");
    }
}

/// <summary>An API whose database has never been migrated.</summary>
public sealed class UnmigratedApiFactory : ApiFactory
{
    protected override bool MigrateOnStartup => false;
}
