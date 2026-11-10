using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astryx.Infrastructure.Outbox;

/// <summary>Runs <see cref="OutboxDispatcher"/> continuously: drains while there is work, then polls.</summary>
public sealed class OutboxDispatcherService(OutboxDispatcher dispatcher, IOptions<OutboxOptions> options, TimeProvider timeProvider, ILogger<OutboxDispatcherService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollInterval, timeProvider);
        do
        {
            try
            {
                while (await dispatcher.DispatchOnceAsync(stoppingToken) > 0 && !stoppingToken.IsCancellationRequested)
                {
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox dispatch cycle failed; retrying on next tick");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
