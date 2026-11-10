using System.Text.Json;
using Astryx.Infrastructure.Outbox;
using Astryx.Infrastructure.Persistence;
using Astryx.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Astryx.Infrastructure.Jobs;

public interface IJobQueue
{
    /// <summary>Enqueues a job. Returns its id, or <c>null</c> when a pending/running job already holds the dedupe key.</summary>
    Task<Guid?> EnqueueAsync<TJob>(TJob job, EnqueueOptions? options = null, CancellationToken cancellationToken = default)
        where TJob : IJob;
}

public sealed class JobQueue(InfraDbContext db, IOptions<JobQueueOptions> defaults, TimeProvider timeProvider) : IJobQueue
{
    public Task<Guid?> EnqueueAsync<TJob>(TJob job, EnqueueOptions? options = null, CancellationToken cancellationToken = default)
        where TJob : IJob =>
        EnqueueRawAsync(db, TJob.JobType, JsonSerializer.Serialize(job, InfrastructureJson.Options), options, defaults.Value, timeProvider, cancellationToken);

    internal static async Task<Guid?> EnqueueRawAsync(
        InfraDbContext db, string type, string payload, EnqueueOptions? options, JobQueueOptions defaults, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var id = Ids.New(timeProvider);
        var runAfter = options?.RunAfter ?? now;
        var maxAttempts = options?.MaxAttempts ?? defaults.DefaultMaxAttempts;
        var timeoutSeconds = (int)Math.Ceiling((options?.Timeout ?? defaults.DefaultTimeout).TotalSeconds);
        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO infra.job_queue (id, type, payload, status, run_after, attempts, max_attempts, timeout_seconds, dedupe_key, correlation_id, created_at)
            VALUES ({id}, {type}, CAST({payload} AS jsonb), 'pending', {runAfter}, 0, {maxAttempts}, {timeoutSeconds}, {options?.DedupeKey}, {CorrelationContext.Id}, {now})
            ON CONFLICT (dedupe_key) WHERE status IN ('pending', 'running') DO NOTHING
            """,
            cancellationToken);
        return inserted == 1 ? id : null;
    }
}
