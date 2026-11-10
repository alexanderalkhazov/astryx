using System.Text.Json;
using Astryx.Contracts.Events;
using Astryx.Infrastructure.Outbox;
using Astryx.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Infrastructure.Persistence;

/// <summary>
/// Base for every module's DbContext: one Postgres schema per module, snake_case names, decimal defaults for money
/// (SYSTEM_DESIGN.md §4.4, §10.1). Derived contexts configure their tables in <see cref="ConfigureModel"/>.
/// </summary>
public abstract class ModuleDbContext(DbContextOptions options) : DbContext(options)
{
    private readonly List<OutboxMessage> _pendingEvents = [];

    /// <summary>The Postgres schema this context owns.</summary>
    public abstract string Schema { get; }

    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        ConfigureModel(modelBuilder);
    }

    protected virtual void ConfigureModel(ModelBuilder modelBuilder)
    {
    }

    /// <summary>
    /// Queues an integration event. It is written to <c>infra.outbox_messages</c> in the same transaction as the
    /// next <see cref="SaveChangesAsync(bool, CancellationToken)"/>, so the event exists if and only if the state change
    /// committed (SYSTEM_DESIGN.md §9.4).
    /// </summary>
    public void Publish<TEvent>(TEvent integrationEvent)
        where TEvent : IIntegrationEvent
    {
        var now = DateTimeOffset.UtcNow;
        _pendingEvents.Add(new OutboxMessage
        {
            Id = Ids.New(),
            OccurredAt = now,
            NextAttemptAt = now,
            Type = TEvent.EventName,
            Payload = JsonSerializer.Serialize(integrationEvent, InfrastructureJson.Options),
            CorrelationId = CorrelationContext.Id,
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        _pendingEvents.Count == 0
            ? base.SaveChanges(acceptAllChangesOnSuccess)
            : throw new InvalidOperationException("Contexts with published events must be saved with SaveChangesAsync.");

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        if (_pendingEvents.Count == 0)
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        var ownsTransaction = Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction ? await Database.BeginTransactionAsync(cancellationToken) : null;

        var written = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        foreach (var message in _pendingEvents)
        {
            await Database.ExecuteSqlAsync(
                $"""
                INSERT INTO infra.outbox_messages (id, occurred_at, type, payload, correlation_id, attempts, next_attempt_at)
                VALUES ({message.Id}, {message.OccurredAt}, {message.Type}, CAST({message.Payload} AS jsonb), {message.CorrelationId}, 0, {message.NextAttemptAt})
                """,
                cancellationToken);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        _pendingEvents.Clear();
        return written;
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Money by default; prices (18,6), quantities (18,6) and ratios (9,6) override per property (§10.1).
        configurationBuilder.Properties<decimal>().HavePrecision(20, 4);
    }
}
