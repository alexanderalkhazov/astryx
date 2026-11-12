using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astryx.Modules.Ai.Application.Jobs;

/// <summary><c>AiAnalysisWorker</c> (§19.2): <c>Ai:MaxConcurrency</c> loops, each draining due jobs, then waiting a poll interval.</summary>
public sealed class AiAnalysisWorker(AnalysisJobProcessor processor, IOptions<AnalysisJobOptions> options, AiConcurrency concurrency, TimeProvider timeProvider, ILogger<AiAnalysisWorker> logger)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(0, Math.Max(1, concurrency.Value)).Select(i => LoopAsync($"ai:{Environment.MachineName}:{Environment.ProcessId}:{i}", stoppingToken)));

    private async Task LoopAsync(string workerId, CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.Value.PollIntervalMs), timeProvider);
        do
        {
            try
            {
                while (!stoppingToken.IsCancellationRequested && await processor.RunOnceAsync(workerId, stoppingToken))
                {
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "AI worker {WorkerId} cycle failed; retrying on next tick", workerId);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

/// <summary><c>Ai:MaxConcurrency</c>: 1 locally, a small model on a laptop (§12.8).</summary>
public sealed record AiConcurrency(int Value);
