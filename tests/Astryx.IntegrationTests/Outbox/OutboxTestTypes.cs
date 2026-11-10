using System.Collections.Concurrent;
using Astryx.Contracts.Events;
using Astryx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.IntegrationTests.Outbox;

public sealed record WidgetCreated(Guid WidgetId, string Name, decimal Price) : IIntegrationEvent
{
    public static string EventName => "test.widget_created.v1";
}

public sealed class Widget
{
    public Guid Id { get; set; }

    public required string Name { get; set; }
}

/// <summary>A stand-in module context with one business table, to prove state and events commit together.</summary>
public sealed class WidgetDbContext(DbContextOptions<WidgetDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "outbox_test";

    public override string Schema => SchemaName;

    public DbSet<Widget> Widgets => Set<Widget>();

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Widget>(e =>
        {
            e.ToTable("widgets");
            e.HasIndex(w => w.Name).IsUnique();
        });
}

/// <summary>Shared across dispatchers in a test: counts deliveries per message and per handler.</summary>
public sealed class DeliveryLog
{
    public ConcurrentDictionary<(Guid MessageId, string Handler), int> Counts { get; } = new();

    public void Record(Guid messageId, string handler) => Counts.AddOrUpdate((messageId, handler), 1, (_, n) => n + 1);

    public int For(Guid messageId, string handler) => Counts.GetValueOrDefault((messageId, handler));
}

public sealed class RecordingHandler(DeliveryLog log) : IIntegrationEventHandler<WidgetCreated>
{
    public Task HandleAsync(WidgetCreated integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken)
    {
        integrationEvent.Price.ShouldBe(186.50m);
        log.Record(context.MessageId, nameof(RecordingHandler));
        return Task.CompletedTask;
    }
}

/// <summary>Fails <see cref="RemainingFailures"/> times, then succeeds (int.MaxValue = always fails).</summary>
public sealed class FailSwitch
{
    public int RemainingFailures { get; set; }
}

public sealed class FlakyHandler(DeliveryLog log, FailSwitch failSwitch) : IIntegrationEventHandler<WidgetCreated>
{
    public Task HandleAsync(WidgetCreated integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken)
    {
        if (failSwitch.RemainingFailures > 0)
        {
            failSwitch.RemainingFailures--;
            throw new InvalidOperationException("downstream unavailable");
        }

        log.Record(context.MessageId, nameof(FlakyHandler));
        return Task.CompletedTask;
    }
}
