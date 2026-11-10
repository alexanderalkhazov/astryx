namespace Astryx.Infrastructure.Jobs;

/// <summary>
/// A unit of background work stored in <c>infra.job_queue</c> (SYSTEM_DESIGN.md §19). <see cref="JobType"/> is
/// persisted with each job, so keep it stable once released (e.g. <c>market.quote_sync.v1</c>).
/// </summary>
public interface IJob
{
    static abstract string JobType { get; }
}

/// <summary>
/// Executes a job. A job can run more than once (retry after failure, or after a lease expires when a worker dies),
/// so handlers must be idempotent.
/// </summary>
public interface IJobHandler<in TJob>
    where TJob : IJob
{
    Task HandleAsync(TJob job, JobContext context, CancellationToken cancellationToken);
}

public sealed record JobContext(Guid JobId, int Attempt, int MaxAttempts, string? CorrelationId);

/// <summary>Per-enqueue settings. Anything unset falls back to <see cref="JobQueueOptions"/>.</summary>
public sealed record EnqueueOptions
{
    public DateTimeOffset? RunAfter { get; init; }

    /// <summary>At most one pending or running job may hold this key; a duplicate enqueue is skipped.</summary>
    public string? DedupeKey { get; init; }

    public int? MaxAttempts { get; init; }

    public TimeSpan? Timeout { get; init; }
}
