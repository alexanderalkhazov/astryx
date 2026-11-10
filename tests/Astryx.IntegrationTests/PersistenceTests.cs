using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astryx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Astryx.IntegrationTests;

public class PersistenceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Every_module_has_its_own_history_table_with_all_its_migrations_applied()
    {
        _ = factory.CreateClient(); // starting the host applies migrations
        var registrations = factory.Services.GetServices<ModuleDbContextRegistration>().ToList();
        registrations.Count.ShouldBe(14); // infra + 13 modules

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        foreach (var registration in registrations)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(registration.ContextType);
            var defined = context.Database.GetMigrations().Count();
            await using var command = new NpgsqlCommand($"SELECT count(*) FROM \"{registration.Schema}\".\"{registration.HistoryTable}\"", connection);

            defined.ShouldBeGreaterThan(0, registration.ContextType.Name);
            ((long)(await command.ExecuteScalarAsync())!).ShouldBe(defined, registration.ContextType.Name);
        }
    }

    [Theory]
    [InlineData("citext")]
    [InlineData("pg_trgm")]
    public async Task Required_extensions_are_installed(string extension)
    {
        _ = factory.CreateClient();

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_extension WHERE extname = @name", connection);
        command.Parameters.AddWithValue("name", extension);

        ((long)(await command.ExecuteScalarAsync())!).ShouldBe(1);
    }

    [Fact]
    public async Task Ready_reports_migrations_healthy_after_startup_migration()
    {
        var body = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/health/ready");

        body.GetProperty("entries").GetProperty("migrations").GetProperty("status").GetString().ShouldBe("Healthy");
    }
}

public class PendingMigrationTests(UnmigratedApiFactory factory) : IClassFixture<UnmigratedApiFactory>
{
    [Fact]
    public async Task Ready_is_unhealthy_while_migrations_are_pending()
    {
        var response = await factory.CreateClient().GetAsync("/health/ready");

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var migrations = body.GetProperty("entries").GetProperty("migrations");
        migrations.GetProperty("status").GetString().ShouldBe("Unhealthy");
        migrations.GetProperty("data").GetProperty("IdentityDbContext").GetArrayLength().ShouldBeGreaterThan(0);
    }
}
