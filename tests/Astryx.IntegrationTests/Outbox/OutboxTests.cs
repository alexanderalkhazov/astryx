using Astryx.Infrastructure.Outbox;
using Astryx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Astryx.IntegrationTests.Outbox;

public sealed class OutboxDatabase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var provider = OutboxTests.BuildProvider(ConnectionString, new DeliveryLog());
        await MigrationRunner.MigrateAllAsync(provider);
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<WidgetDbContext>().GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();
}

/// <summary>Transactional outbox (T-014, SYSTEM_DESIGN.md §9.4): atomic publish, exactly-once handling, retries, poison.</summary>
public class OutboxTests(OutboxDatabase database) : IClassFixture<OutboxDatabase>, IAsyncLifetime
{
    private readonly DeliveryLog _log = new();

    internal static ServiceProvider BuildProvider(string connectionString, DeliveryLog log, FailSwitch? failSwitch = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = connectionString,
            ["Outbox:BaseBackoff"] = "00:00:00",
            ["Outbox:MaxAttempts"] = "3",
        }).Build();

        var services = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(configuration);
        services.AddAstryxPersistence(configuration);
        services.AddModuleDbContext<WidgetDbContext>(WidgetDbContext.SchemaName, historyKey: "outbox_test");
        services.AddSingleton(log);
        services.AddIntegrationEventHandler<WidgetCreated, RecordingHandler>();
        if (failSwitch is not null)
        {
            services.AddSingleton(failSwitch);
            services.AddIntegrationEventHandler<WidgetCreated, FlakyHandler>();
        }

        return services.BuildServiceProvider();
    }

    public async Task InitializeAsync()
    {
        await using var provider = BuildProvider(database.ConnectionString, _log);
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<InfraDbContext>().Database
            .ExecuteSqlRawAsync("TRUNCATE infra.outbox_messages CASCADE; TRUNCATE outbox_test.widgets;");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<Guid> CreateWidgetAsync(ServiceProvider provider, string name)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WidgetDbContext>();
        var widget = new Widget { Id = Guid.CreateVersion7(), Name = name };
        db.Widgets.Add(widget);
        db.Publish(new WidgetCreated(widget.Id, name, 186.50m));
        await db.SaveChangesAsync();
        return widget.Id;
    }

    private static async Task<List<OutboxMessage>> MessagesAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<InfraDbContext>().OutboxMessages.AsNoTracking().OrderBy(m => m.OccurredAt).ToListAsync();
    }

    private static async Task<int> CountWidgetsAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<WidgetDbContext>().Widgets.CountAsync();
    }

    private static Task<int> DispatchAsync(ServiceProvider provider) =>
        provider.GetRequiredService<OutboxDispatcher>().DispatchOnceAsync(CancellationToken.None);

    [Fact]
    public async Task Published_event_commits_with_the_state_change()
    {
        await using var provider = BuildProvider(database.ConnectionString, _log);
        CorrelationContext.Id = "corr-outbox-0001";
        try
        {
            await CreateWidgetAsync(provider, "nvda");
        }
        finally
        {
            CorrelationContext.Id = null;
        }

        (await CountWidgetsAsync(provider)).ShouldBe(1);
        var message = (await MessagesAsync(provider)).ShouldHaveSingleItem();
        message.Type.ShouldBe("test.widget_created.v1");
        message.Payload.ShouldContain("\"price\": \"186.50\"");
        message.CorrelationId.ShouldBe("corr-outbox-0001");
        message.ProcessedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Rolled_back_transaction_publishes_nothing()
    {
        await using var provider = BuildProvider(database.ConnectionString, _log);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WidgetDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.Widgets.Add(new Widget { Id = Guid.CreateVersion7(), Name = "rolled-back" });
            db.Publish(new WidgetCreated(Guid.CreateVersion7(), "rolled-back", 186.50m));
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        (await CountWidgetsAsync(provider)).ShouldBe(0);
        (await MessagesAsync(provider)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Failed_save_publishes_nothing()
    {
        await using var provider = BuildProvider(database.ConnectionString, _log);
        await CreateWidgetAsync(provider, "unique-name");

        await Should.ThrowAsync<DbUpdateException>(() => CreateWidgetAsync(provider, "unique-name"));

        (await MessagesAsync(provider)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Event_committed_before_a_crash_is_delivered_exactly_once()
    {
        await using (var crashed = BuildProvider(database.ConnectionString, _log))
        {
            await CreateWidgetAsync(crashed, "survives-crash");
        } // process "crashes" here: committed, never dispatched

        await using var restarted = BuildProvider(database.ConnectionString, _log);
        (await DispatchAsync(restarted)).ShouldBe(1);
        (await DispatchAsync(restarted)).ShouldBe(0);

        var message = (await MessagesAsync(restarted)).ShouldHaveSingleItem();
        message.ProcessedAt.ShouldNotBeNull();
        _log.For(message.Id, nameof(RecordingHandler)).ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_dispatchers_handle_each_message_once()
    {
        await using var writer = BuildProvider(database.ConnectionString, _log);
        for (var i = 0; i < 40; i++)
        {
            await CreateWidgetAsync(writer, $"w-{i}");
        }

        await using var first = BuildProvider(database.ConnectionString, _log);
        await using var second = BuildProvider(database.ConnectionString, _log);
        static async Task DrainAsync(ServiceProvider p)
        {
            while (await DispatchAsync(p) > 0)
            {
            }
        }

        await Task.WhenAll(DrainAsync(first), DrainAsync(second), DrainAsync(first), DrainAsync(second));

        var messages = await MessagesAsync(writer);
        messages.Count.ShouldBe(40);
        messages.ShouldAllBe(m => m.ProcessedAt != null);
        messages.ShouldAllBe(m => _log.For(m.Id, nameof(RecordingHandler)) == 1);
    }

    [Fact]
    public async Task Retry_reruns_only_the_handler_that_failed()
    {
        var failSwitch = new FailSwitch { RemainingFailures = 1 };
        await using var provider = BuildProvider(database.ConnectionString, _log, failSwitch);
        await CreateWidgetAsync(provider, "partial");

        await DispatchAsync(provider);
        var afterFirst = (await MessagesAsync(provider)).ShouldHaveSingleItem();
        afterFirst.ProcessedAt.ShouldBeNull();
        afterFirst.Attempts.ShouldBe(1);
        afterFirst.LastError!.ShouldContain("downstream unavailable");

        await DispatchAsync(provider);
        var afterSecond = (await MessagesAsync(provider)).ShouldHaveSingleItem();
        afterSecond.ProcessedAt.ShouldNotBeNull();
        _log.For(afterSecond.Id, nameof(RecordingHandler)).ShouldBe(1);
        _log.For(afterSecond.Id, nameof(FlakyHandler)).ShouldBe(1);
    }

    [Fact]
    public async Task Message_becomes_poison_after_max_attempts()
    {
        await using var provider = BuildProvider(database.ConnectionString, _log, new FailSwitch { RemainingFailures = int.MaxValue });
        await CreateWidgetAsync(provider, "always-fails");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            (await DispatchAsync(provider)).ShouldBe(1);
        }

        (await DispatchAsync(provider)).ShouldBe(0);
        var message = (await MessagesAsync(provider)).ShouldHaveSingleItem();
        message.Attempts.ShouldBe(3);
        message.FailedAt.ShouldNotBeNull();
        message.ProcessedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Undeserialisable_payload_is_poison_immediately()
    {
        await using var provider = BuildProvider(database.ConnectionString, _log);
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<InfraDbContext>().Database.ExecuteSqlAsync($$"""
                INSERT INTO infra.outbox_messages (id, occurred_at, type, payload, attempts, next_attempt_at)
                VALUES ({{Guid.CreateVersion7()}}, now(), 'test.widget_created.v1', '{"widgetId":"not-a-guid"}'::jsonb, 0, now())
                """);
        }

        (await DispatchAsync(provider)).ShouldBe(1);

        var message = (await MessagesAsync(provider)).ShouldHaveSingleItem();
        message.FailedAt.ShouldNotBeNull();
        message.Attempts.ShouldBe(1);
        message.LastError!.ShouldStartWith("Undeserialisable payload");
    }

    [Fact]
    public async Task Event_without_handlers_is_marked_processed()
    {
        await using var provider = BuildProvider(database.ConnectionString, _log);
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<InfraDbContext>().Database.ExecuteSqlAsync($$"""
                INSERT INTO infra.outbox_messages (id, occurred_at, type, payload, attempts, next_attempt_at)
                VALUES ({{Guid.CreateVersion7()}}, now(), 'test.nobody_listens.v1', '{}'::jsonb, 0, now())
                """);
        }

        (await DispatchAsync(provider)).ShouldBe(1);
        (await MessagesAsync(provider)).ShouldHaveSingleItem().ProcessedAt.ShouldNotBeNull();
    }
}
