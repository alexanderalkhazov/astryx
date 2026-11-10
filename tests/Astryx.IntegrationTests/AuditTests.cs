using System.Text.Json;
using Astryx.Contracts.Audit;
using Astryx.Infrastructure.Jobs;
using Astryx.Infrastructure.Outbox;
using Astryx.Modules.Audit.Application;
using Astryx.Modules.Audit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Astryx.IntegrationTests;

/// <summary>Audit module (T-017, SYSTEM_DESIGN.md §7.18, §10.4).</summary>
public class AuditTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private AsyncServiceScope Scope()
    {
        _ = factory.CreateClient(); // host started, migrations applied
        return factory.Services.CreateAsyncScope();
    }

    private async Task<long> CountAsync(string entityId)
    {
        await using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        return await db.AuditLogs.LongCountAsync(a => a.EntityId == entityId);
    }

    private static AuditEntry Entry(string entityId, object? before = null, object? after = null) =>
        new("risk_settings.updated", AuditActor.User, Guid.CreateVersion7(), "risk_settings", entityId, before, after);

    [Fact]
    public async Task Audit_row_rolls_back_with_its_transaction()
    {
        var entityId = Guid.NewGuid().ToString();
        await using (var scope = Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.Audit(Entry(entityId));
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        (await CountAsync(entityId)).ShouldBe(0);
    }

    [Fact]
    public async Task Audit_row_commits_with_the_change_and_keeps_the_correlation_id()
    {
        var entityId = Guid.NewGuid().ToString();
        CorrelationContext.Id = "corr-audit-0001";
        try
        {
            await using var scope = Scope();
            var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            db.Audit(Entry(entityId, after: new { maxPositionWeightPct = 0.12m }));
            await db.SaveChangesAsync();
        }
        finally
        {
            CorrelationContext.Id = null;
        }

        await using var read = Scope();
        var row = await read.ServiceProvider.GetRequiredService<AuditDbContext>().AuditLogs.AsNoTracking().SingleAsync(a => a.EntityId == entityId);
        row.CorrelationId.ShouldBe("corr-audit-0001");
        row.Action.ShouldBe("risk_settings.updated");
        JsonDocument.Parse(row.After!).RootElement.GetProperty("maxPositionWeightPct").GetString().ShouldBe("0.12");
    }

    [Fact]
    public async Task Standalone_writes_are_redacted()
    {
        var entityId = Guid.NewGuid().ToString();
        await using (var scope = Scope())
        {
            await scope.ServiceProvider.GetRequiredService<IAuditLog>().RecordAsync(new AuditEntry(
                "auth.login_failed", AuditActor.User, EntityType: "user", EntityId: entityId,
                Metadata: new { email = "trader@example.com", password = "correct horse battery staple", attempt = 3 }));
        }

        await using var read = Scope();
        var row = await read.ServiceProvider.GetRequiredService<AuditDbContext>().AuditLogs.AsNoTracking().SingleAsync(a => a.EntityId == entityId);
        var metadata = JsonDocument.Parse(row.Metadata!).RootElement;
        metadata.GetProperty("password").GetString().ShouldBe("[REDACTED]");
        metadata.GetProperty("email").GetString().ShouldBe("t***@example.com");
        metadata.GetProperty("attempt").GetInt32().ShouldBe(3);
        row.Metadata!.ShouldNotContain("battery");
    }

    [Theory]
    [InlineData("UPDATE audit.audit_logs SET action = 'tampered'")]
    [InlineData("DELETE FROM audit.audit_logs")]
    [InlineData("TRUNCATE audit.audit_logs")]
    public async Task Audit_log_is_append_only(string sql)
    {
        await using (var scope = Scope())
        {
            await scope.ServiceProvider.GetRequiredService<IAuditLog>().RecordAsync(Entry(Guid.NewGuid().ToString()));
        }

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        var error = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        error.MessageText.ShouldContain("append-only");
    }

    [Fact]
    public async Task Rows_land_in_the_current_month_partition_and_future_months_exist()
    {
        var entityId = Guid.NewGuid().ToString();
        await using (var scope = Scope())
        {
            await scope.ServiceProvider.GetRequiredService<IAuditLog>().RecordAsync(Entry(entityId));
        }

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var where = new NpgsqlCommand("SELECT tableoid::regclass::text FROM audit.audit_logs WHERE entity_id = @id", connection);
        where.Parameters.AddWithValue("id", entityId);
        var now = DateTimeOffset.UtcNow;
        ((string)(await where.ExecuteScalarAsync())!).ShouldBe($"audit.audit_logs_y{now:yyyy}m{now:MM}");

        foreach (var offset in new[] { 1, 2 })
        {
            var month = new DateOnly(now.Year, now.Month, 1).AddMonths(offset);
            await using var exists = new NpgsqlCommand("SELECT to_regclass(@name) IS NOT NULL", connection);
            exists.Parameters.AddWithValue("name", $"audit.{AuditPartitionMaintenanceHandler.PartitionName(month)}");
            ((bool)(await exists.ExecuteScalarAsync())!).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Maintenance_drops_partitions_past_retention_and_is_idempotent()
    {
        await using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        await db.Database.ExecuteSqlAsync($"SELECT audit.ensure_month_partition({new DateOnly(2020, 1, 1)})");
        var handler = new AuditPartitionMaintenanceHandler(db, TimeProvider.System, NullLogger<AuditPartitionMaintenanceHandler>.Instance);
        var context = new JobContext(Guid.NewGuid(), 1, 3, null);

        await handler.HandleAsync(new AuditPartitionMaintenanceJob(), context, CancellationToken.None);
        await handler.HandleAsync(new AuditPartitionMaintenanceJob(), context, CancellationToken.None);

        (await db.Database.SqlQuery<bool>($"SELECT to_regclass('audit.audit_logs_y2020m01') IS NULL AS \"Value\"").SingleAsync()).ShouldBeTrue();
        (await db.Database.SqlQuery<bool>($"SELECT to_regclass('audit.audit_logs_default') IS NOT NULL AS \"Value\"").SingleAsync()).ShouldBeTrue();
    }

    [Fact]
    public void Maintenance_runs_daily()
    {
        _ = factory.CreateClient();
        var registry = factory.Services.GetRequiredService<JobRegistry>();

        registry.Recurring.Single(r => r.Name == "audit-partition-maintenance").Interval.ShouldBe(TimeSpan.FromDays(1));
        registry.HandlerFor(AuditPartitionMaintenanceJob.JobType).ShouldNotBeNull();
    }
}
