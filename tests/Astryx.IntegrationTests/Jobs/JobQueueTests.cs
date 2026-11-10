using System.Collections.Concurrent;
using Astryx.Infrastructure.Jobs;
using Astryx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace Astryx.IntegrationTests.Jobs;

public sealed record PingJob(string Label, decimal Amount) : IJob
{
    public static string JobType => "test.ping.v1";
}

/// <summary>Shared behaviour switches and a log of executions per job id.</summary>
public sealed class JobProbe
{
    public ConcurrentDictionary<Guid, int> Runs { get; } = new();

    public int FailuresRemaining { get; set; }

    public TimeSpan Delay { get; set; }

    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class PingHandler(JobProbe probe) : IJobHandler<PingJob>
{
    public async Task HandleAsync(PingJob job, JobContext context, CancellationToken cancellationToken)
    {
        job.Amount.ShouldBe(186.50m);
        probe.Started.TrySetResult();
        if (probe.Delay > TimeSpan.Zero)
        {
            await Task.Delay(probe.Delay, cancellationToken);
        }

        if (probe.FailuresRemaining > 0)
        {
            probe.FailuresRemaining--;
            throw new InvalidOperationException("provider returned 503");
        }

        probe.Runs.AddOrUpdate(context.JobId, 1, (_, n) => n + 1);
    }
}

public sealed class JobDatabase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var provider = JobQueueTests.BuildProvider(ConnectionString, new JobProbe(), new FakeTimeProvider());
        await MigrationRunner.MigrateAllAsync(provider);
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();
}

/// <summary>Postgres job queue, scheduler and reaper (T-015, SYSTEM_DESIGN.md §19).</summary>
public class JobQueueTests(JobDatabase database) : IClassFixture<JobDatabase>, IAsyncLifetime
{
    private readonly JobProbe _probe = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 10, 14, 32, 0, TimeSpan.Zero));

    internal static ServiceProvider BuildProvider(string connectionString, JobProbe probe, TimeProvider time)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = connectionString,
            ["Jobs:BaseBackoff"] = "00:00:30",
            ["Jobs:LeaseGrace"] = "00:00:30",
        }).Build();

        var services = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(configuration).AddSingleton(time);
        services.AddAstryxPersistence(configuration);
        services.AddSingleton(probe);
        services.AddJobHandler<PingJob, PingHandler>();
        services.AddRecurringJob("ping-every-minute", TimeSpan.FromMinutes(1), slot => new PingJob($"slot {slot:HH:mm}", 186.50m));
        return services.BuildServiceProvider();
    }

    private ServiceProvider Provider() => BuildProvider(database.ConnectionString, _probe, _time);

    public async Task InitializeAsync()
    {
        await using var provider = Provider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<InfraDbContext>().Database
            .ExecuteSqlRawAsync("TRUNCATE infra.job_queue; TRUNCATE infra.recurring_jobs;");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<Guid?> EnqueueAsync(ServiceProvider provider, EnqueueOptions? options = null)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IJobQueue>().EnqueueAsync(new PingJob("hello", 186.50m), options);
    }

    private static async Task<List<JobRecord>> JobsAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<InfraDbContext>().JobQueue.AsNoTracking().OrderBy(j => j.CreatedAt).ToListAsync();
    }

    private static Task<bool> RunAsync(ServiceProvider provider, string worker = "worker-a") =>
        provider.GetRequiredService<JobRunner>().RunOnceAsync(worker, CancellationToken.None);

    [Fact]
    public async Task Enqueued_job_runs_and_succeeds()
    {
        await using var provider = Provider();
        var id = await EnqueueAsync(provider);

        (await RunAsync(provider)).ShouldBeTrue();
        (await RunAsync(provider)).ShouldBeFalse();

        var job = (await JobsAsync(provider)).ShouldHaveSingleItem();
        job.Id.ShouldBe(id!.Value);
        job.Status.ShouldBe(JobStatus.Succeeded);
        job.Attempts.ShouldBe(1);
        job.FinishedAt.ShouldNotBeNull();
        job.LockedBy.ShouldBeNull();
        _probe.Runs[job.Id].ShouldBe(1);
    }

    [Fact]
    public async Task Two_workers_racing_for_one_job_only_one_wins()
    {
        _probe.Delay = TimeSpan.FromMilliseconds(300);
        await using var first = Provider();
        await using var second = Provider();
        await EnqueueAsync(first);

        var results = await Task.WhenAll(RunAsync(first, "worker-a"), RunAsync(second, "worker-b"));

        results.Count(r => r).ShouldBe(1);
        var job = (await JobsAsync(first)).ShouldHaveSingleItem();
        job.Status.ShouldBe(JobStatus.Succeeded);
        _probe.Runs[job.Id].ShouldBe(1);
    }

    [Fact]
    public async Task Expired_lease_is_requeued_and_the_job_completes_on_another_worker()
    {
        await using var provider = Provider();
        await EnqueueAsync(provider, new EnqueueOptions { Timeout = TimeSpan.FromMinutes(1) });
        var runner = provider.GetRequiredService<JobRunner>();

        var claimed = await runner.TryClaimAsync("worker-that-dies", CancellationToken.None);
        claimed.ShouldNotBeNull();
        (await runner.ReapExpiredLeasesAsync(CancellationToken.None)).ShouldBe(0); // lease still valid

        _time.Advance(TimeSpan.FromSeconds(91)); // timeout 60 s + grace 30 s
        (await runner.ReapExpiredLeasesAsync(CancellationToken.None)).ShouldBe(1);
        var requeued = (await JobsAsync(provider)).ShouldHaveSingleItem();
        requeued.Status.ShouldBe(JobStatus.Pending);
        requeued.LastError!.ShouldContain("Lease expired");

        (await RunAsync(provider, "worker-b")).ShouldBeTrue();
        var done = (await JobsAsync(provider)).ShouldHaveSingleItem();
        done.Status.ShouldBe(JobStatus.Succeeded);
        done.Attempts.ShouldBe(2);

        // The dead worker cannot overwrite the result afterwards.
        await runner.ExecuteAsync(claimed, "worker-that-dies", CancellationToken.None);
        (await JobsAsync(provider)).ShouldHaveSingleItem().Attempts.ShouldBe(2);
    }

    [Fact]
    public async Task Dedupe_key_prevents_a_second_active_job()
    {
        await using var provider = Provider();
        var options = new EnqueueOptions { DedupeKey = "scanner-run:active" };

        (await EnqueueAsync(provider, options)).ShouldNotBeNull();
        (await EnqueueAsync(provider, options)).ShouldBeNull();
        (await JobsAsync(provider)).Count.ShouldBe(1);

        await RunAsync(provider);
        (await EnqueueAsync(provider, options)).ShouldNotBeNull(); // key is free once the job finished
        (await JobsAsync(provider)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Failing_job_retries_with_backoff_then_goes_dead()
    {
        _probe.FailuresRemaining = int.MaxValue;
        await using var provider = Provider();
        await EnqueueAsync(provider, new EnqueueOptions { MaxAttempts = 3 });

        (await RunAsync(provider)).ShouldBeTrue();
        var afterFirst = (await JobsAsync(provider)).ShouldHaveSingleItem();
        afterFirst.Status.ShouldBe(JobStatus.Pending);
        afterFirst.RunAfter.ShouldBe(_time.GetUtcNow() + TimeSpan.FromSeconds(30));
        afterFirst.LastError!.ShouldContain("provider returned 503");

        (await RunAsync(provider)).ShouldBeFalse(); // not due yet
        _time.Advance(TimeSpan.FromSeconds(30));
        (await RunAsync(provider)).ShouldBeTrue();
        (await JobsAsync(provider)).ShouldHaveSingleItem().RunAfter.ShouldBe(_time.GetUtcNow() + TimeSpan.FromSeconds(60));

        _time.Advance(TimeSpan.FromSeconds(60));
        (await RunAsync(provider)).ShouldBeTrue();
        var dead = (await JobsAsync(provider)).ShouldHaveSingleItem();
        dead.Status.ShouldBe(JobStatus.Dead);
        dead.Attempts.ShouldBe(3);
        dead.FinishedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Timed_out_job_is_retried()
    {
        _probe.Delay = TimeSpan.FromSeconds(5);
        await using var provider = Provider();
        await EnqueueAsync(provider, new EnqueueOptions { Timeout = TimeSpan.FromSeconds(1), MaxAttempts = 2 });

        await RunAsync(provider);

        var job = (await JobsAsync(provider)).ShouldHaveSingleItem();
        job.Status.ShouldBe(JobStatus.Pending);
        job.LastError.ShouldBe("Timed out after 1 s");
    }

    [Fact]
    public async Task Unknown_job_type_goes_dead_immediately()
    {
        await using var provider = Provider();
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<InfraDbContext>().Database.ExecuteSqlAsync($$"""
                INSERT INTO infra.job_queue (id, type, payload, status, run_after, attempts, max_attempts, timeout_seconds, created_at)
                VALUES ({{Guid.CreateVersion7()}}, 'test.retired_job.v1', '{}'::jsonb, 'pending', {{_time.GetUtcNow()}}, 0, 3, 60, {{_time.GetUtcNow()}})
                """);
        }

        (await RunAsync(provider)).ShouldBeTrue();
        var job = (await JobsAsync(provider)).ShouldHaveSingleItem();
        job.Status.ShouldBe(JobStatus.Dead);
        job.LastError!.ShouldContain("No handler registered");
    }

    [Fact]
    public async Task Recurring_slot_is_enqueued_once_across_replicas()
    {
        await using var replicaA = Provider();
        await using var replicaB = Provider();
        var schedulerA = replicaA.GetRequiredService<JobScheduler>();
        var schedulerB = replicaB.GetRequiredService<JobScheduler>();
        async Task<int> PingJobsAsync() => (await JobsAsync(replicaA)).Count(j => j.Type == PingJob.JobType);

        // Other schedules (e.g. idempotency cleanup) also exist; only this schedule's jobs are counted.
        await Task.WhenAll(schedulerA.TickAsync(CancellationToken.None), schedulerB.TickAsync(CancellationToken.None), schedulerA.TickAsync(CancellationToken.None));
        (await PingJobsAsync()).ShouldBe(1);

        while (await RunAsync(replicaA))
        {
        } // even after the slot's job finishes...

        await schedulerB.TickAsync(CancellationToken.None);
        (await PingJobsAsync()).ShouldBe(1); // ...the same slot is not enqueued again

        _time.Advance(TimeSpan.FromMinutes(1));
        await Task.WhenAll(schedulerA.TickAsync(CancellationToken.None), schedulerB.TickAsync(CancellationToken.None));
        (await PingJobsAsync()).ShouldBe(2);
    }
}
