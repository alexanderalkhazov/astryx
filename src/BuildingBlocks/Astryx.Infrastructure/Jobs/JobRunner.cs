using System.Diagnostics;
using System.Text.Json;
using Astryx.Infrastructure.Observability;
using Astryx.Infrastructure.Outbox;
using Astryx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astryx.Infrastructure.Jobs;

/// <summary>
/// Claims, executes and settles jobs from <c>infra.job_queue</c> (SYSTEM_DESIGN.md §19.1–19.3).
/// Claims use <c>FOR UPDATE SKIP LOCKED</c>, so concurrent workers never take the same job; each claim carries a lease,
/// and settling a job only succeeds while the worker still holds that lease.
/// </summary>
public sealed class JobRunner(
    IServiceProvider services,
    JobRegistry registry,
    IOptions<JobQueueOptions> options,
    TimeProvider timeProvider,
    ILogger<JobRunner> logger)
{
    /// <summary>Claims and executes the next due job. Returns false when nothing was due.</summary>
    public async Task<bool> RunOnceAsync(string workerId, CancellationToken cancellationToken)
    {
        var job = await TryClaimAsync(workerId, cancellationToken);
        if (job is null)
        {
            return false;
        }

        await ExecuteAsync(job, workerId, cancellationToken);
        return true;
    }

    /// <summary>Marks the next due pending job as running for <paramref name="workerId"/> and returns it.</summary>
    public async Task<JobRecord?> TryClaimAsync(string workerId, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraDbContext>();
        var now = timeProvider.GetUtcNow();
        var grace = options.Value.LeaseGrace;
        var claimed = await db.JobQueue
            .FromSql($"""
                UPDATE infra.job_queue
                SET status = 'running', locked_by = {workerId}, attempts = attempts + 1, started_at = {now},
                    locked_until = {now} + make_interval(secs => timeout_seconds) + {grace}
                WHERE id = (
                    SELECT id FROM infra.job_queue
                    WHERE status = 'pending' AND run_after <= {now}
                    ORDER BY run_after, id
                    FOR UPDATE SKIP LOCKED
                    LIMIT 1)
                RETURNING *
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return claimed.SingleOrDefault();
    }

    public async Task ExecuteAsync(JobRecord job, string workerId, CancellationToken cancellationToken)
    {
        var handler = registry.HandlerFor(job.Type);
        using var logScope = logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = job.CorrelationId, ["JobId"] = job.Id, ["JobType"] = job.Type });
        if (handler is null)
        {
            await SettleAsync(job, workerId, JobStatus.Dead, $"No handler registered for job type '{job.Type}'.", null, cancellationToken);
            logger.LogError("Job {JobId} has unknown type {JobType}; marked dead", job.Id, job.Type);
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(job.TimeoutSeconds));
        var previousCorrelation = CorrelationContext.Id;
        CorrelationContext.Id = job.CorrelationId;
        using var activity = AstryxTelemetry.Source.StartActivity($"job {job.Type}", ActivityKind.Internal);
        activity?.SetTag("astryx.correlation_id", job.CorrelationId);
        activity?.SetTag("astryx.job_attempt", job.Attempts);
        var started = timeProvider.GetTimestamp();
        try
        {
            await using var handlerScope = services.CreateAsyncScope();
            await handler.InvokeAsync(handlerScope.ServiceProvider, job.Payload, new JobContext(job.Id, job.Attempts, job.MaxAttempts, job.CorrelationId), timeout.Token);
            await SettleAsync(job, workerId, JobStatus.Succeeded, null, null, CancellationToken.None);
            logger.LogInformation("Job {JobId} ({JobType}) succeeded on attempt {Attempt} in {ElapsedMs} ms", job.Id, job.Type, job.Attempts, timeProvider.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutdown: leave the job running; the reaper requeues it once the lease expires (§19.5).
            logger.LogWarning("Job {JobId} interrupted by shutdown; it will be reclaimed after its lease expires", job.Id);
            throw;
        }
        catch (JsonException ex)
        {
            await SettleAsync(job, workerId, JobStatus.Dead, $"Undeserialisable payload: {ex.Message}", null, CancellationToken.None);
            logger.LogError(ex, "Job {JobId} ({JobType}) has an undeserialisable payload; marked dead", job.Id, job.Type);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            AstryxTelemetry.JobsFailed.Add(1, new KeyValuePair<string, object?>("type", job.Type));
            var error = ex is OperationCanceledException ? $"Timed out after {job.TimeoutSeconds} s" : $"{ex.GetType().Name}: {ex.Message}";
            if (job.Attempts >= job.MaxAttempts)
            {
                await SettleAsync(job, workerId, JobStatus.Dead, error, null, CancellationToken.None);
                logger.LogError(ex, "Job {JobId} ({JobType}) is dead after {Attempts} attempts", job.Id, job.Type, job.Attempts);
            }
            else
            {
                var retryAt = timeProvider.GetUtcNow() + Backoff(job.Attempts, options.Value);
                await SettleAsync(job, workerId, JobStatus.Pending, error, retryAt, CancellationToken.None);
                logger.LogWarning(ex, "Job {JobId} ({JobType}) failed attempt {Attempt}; retrying at {RetryAt}", job.Id, job.Type, job.Attempts, retryAt);
            }
        }
        finally
        {
            CorrelationContext.Id = previousCorrelation;
        }
    }

    /// <summary>
    /// Requeues running jobs whose lease expired (the worker died or hung), or marks them dead when out of attempts.
    /// Returns the number of jobs reclaimed.
    /// </summary>
    public async Task<int> ReapExpiredLeasesAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraDbContext>();
        var now = timeProvider.GetUtcNow();
        return await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE infra.job_queue
            SET status = CASE WHEN attempts >= max_attempts THEN 'dead' ELSE 'pending' END,
                finished_at = CASE WHEN attempts >= max_attempts THEN {now} ELSE NULL END,
                run_after = {now}, locked_by = NULL, locked_until = NULL,
                last_error = 'Lease expired (worker stopped or exceeded its timeout)'
            WHERE status = 'running' AND locked_until < {now}
            """,
            cancellationToken);
    }

    private async Task SettleAsync(JobRecord job, string workerId, string status, string? error, DateTimeOffset? retryAt, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraDbContext>();
        var now = timeProvider.GetUtcNow();
        var finishedAt = status == JobStatus.Pending ? (DateTimeOffset?)null : now;
        var truncated = error is { Length: > 2000 } ? error[..2000] : error;
        var updated = await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE infra.job_queue
            SET status = {status}, last_error = COALESCE({truncated}, last_error), finished_at = {finishedAt},
                run_after = COALESCE({retryAt}, run_after), locked_by = NULL, locked_until = NULL
            WHERE id = {job.Id} AND status = 'running' AND locked_by = {workerId}
            """,
            cancellationToken);
        if (updated == 0)
        {
            logger.LogWarning("Job {JobId} was reclaimed before worker {WorkerId} could settle it as {Status}", job.Id, workerId, status);
        }
    }

    internal static TimeSpan Backoff(int attempts, JobQueueOptions settings)
    {
        var exponent = Math.Min(Math.Max(attempts - 1, 0), 20);
        var delay = settings.BaseBackoff * (1L << exponent);
        return delay > settings.MaxBackoff ? settings.MaxBackoff : delay;
    }
}
