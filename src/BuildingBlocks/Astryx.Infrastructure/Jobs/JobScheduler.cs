using Astryx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astryx.Infrastructure.Jobs;

/// <summary>
/// Enqueues recurring jobs. Each schedule has one row in <c>infra.recurring_jobs</c>; a replica advances
/// <c>next_due_at</c> with a conditional UPDATE and enqueues in the same transaction, so exactly one replica
/// enqueues each slot even when several tick at once (SYSTEM_DESIGN.md §19.1).
/// </summary>
public sealed class JobScheduler(
    IServiceProvider services,
    JobRegistry registry,
    IOptions<JobQueueOptions> options,
    TimeProvider timeProvider,
    ILogger<JobScheduler> logger)
{
    /// <summary>Enqueues every due slot once. Returns the number of jobs enqueued by this replica.</summary>
    public async Task<int> TickAsync(CancellationToken cancellationToken)
    {
        var enqueued = 0;
        foreach (var definition in registry.Recurring)
        {
            enqueued += await TickScheduleAsync(definition, cancellationToken) ? 1 : 0;
        }

        return enqueued;
    }

    private async Task<bool> TickScheduleAsync(RecurringJobDefinition definition, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraDbContext>();
        var now = timeProvider.GetUtcNow();
        var slot = Slot(now, definition.Interval);

        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO infra.recurring_jobs (name, next_due_at) VALUES ({definition.Name}, {slot}) ON CONFLICT (name) DO NOTHING",
            cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var next = slot + definition.Interval;
        var claimed = await db.Database.ExecuteSqlAsync(
            $"UPDATE infra.recurring_jobs SET next_due_at = {next} WHERE name = {definition.Name} AND next_due_at <= {now}",
            cancellationToken);
        if (claimed == 0)
        {
            return false;
        }

        await JobQueue.EnqueueRawAsync(
            db,
            definition.JobType,
            definition.CreatePayload(slot),
            new EnqueueOptions { DedupeKey = $"recurring:{definition.Name}" },
            options.Value,
            timeProvider,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogDebug("Enqueued recurring job {Schedule} for slot {Slot}", definition.Name, slot);
        return true;
    }

    /// <summary>Start of the interval containing <paramref name="now"/>, aligned to the Unix epoch.</summary>
    internal static DateTimeOffset Slot(DateTimeOffset now, TimeSpan interval) =>
        new(now.UtcTicks - (now.UtcTicks % interval.Ticks), TimeSpan.Zero);
}
