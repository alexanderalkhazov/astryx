using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace Astryx.IntegrationTests;

public class PersistenceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    public static TheoryData<string, string> Histories => new()
    {
        { "infra", "infra" }, { "identity", "identity" }, { "identity", "profiles" }, { "market", "marketdata" },
        { "market", "quant" }, { "discovery", "discovery" }, { "watchlist", "watchlists" }, { "ai", "ai" },
        { "strategy", "strategies" }, { "trading", "trading" }, { "trading", "paper" }, { "portfolio", "portfolio" },
        { "notify", "notifications" }, { "audit", "audit" },
    };

    [Theory]
    [MemberData(nameof(Histories))]
    public async Task Every_module_schema_has_its_own_applied_migration_history(string schema, string historyKey)
    {
        _ = factory.CreateClient(); // starting the host applies migrations

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM \"{schema}\".\"__ef_migrations_{historyKey}\"", connection);

        ((long)(await command.ExecuteScalarAsync())!).ShouldBe(1);
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
        migrations.GetProperty("data").GetProperty("IdentityDbContext").GetArrayLength().ShouldBe(1);
    }
}
