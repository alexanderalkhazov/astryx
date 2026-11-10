using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astryx.Infrastructure.Jobs;

/// <summary>Runs <see cref="JobQueueOptions.Concurrency"/> claim loops; each drains due jobs, then waits a poll interval.</summary>
public sealed class JobRunnerService(JobRunner runner, IOptions<JobQueueOptions> options, TimeProvider timeProvider, ILogger<JobRunnerService> logger)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(0, Math.Max(1, options.Value.Concurrency)).Select(i => LoopAsync($"{Environment.MachineName}:{Environment.ProcessId}:{i}", stoppingToken)));

    private async Task LoopAsync(string workerId, CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollInterval, timeProvider);
        do
        {
            try
            {
                while (!stoppingToken.IsCancellationRequested && await runner.RunOnceAsync(workerId, stoppingToken))
                {
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Job worker {WorkerId} cycle failed; retrying on next tick", workerId);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

/// <summary>Ticks the recurring scheduler every poll interval and reaps expired leases every reap interval.</summary>
public sealed class JobMaintenanceService(JobScheduler scheduler, JobRunner runner, IOptions<JobQueueOptions> options, TimeProvider timeProvider, ILogger<JobMaintenanceService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollInterval, timeProvider);
        var nextReap = timeProvider.GetUtcNow();
        do
        {
            try
            {
                await scheduler.TickAsync(stoppingToken);
                if (timeProvider.GetUtcNow() >= nextReap)
                {
                    var reaped = await runner.ReapExpiredLeasesAsync(stoppingToken);
                    if (reaped > 0)
                    {
                        logger.LogWarning("Reaped {Count} job(s) with expired leases", reaped);
                    }

                    nextReap = timeProvider.GetUtcNow() + options.Value.ReapInterval;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Job maintenance cycle failed; retrying on next tick");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
