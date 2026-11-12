using Astryx.Modules.MarketData.Application.Providers;
using Microsoft.Extensions.Logging;

namespace Astryx.Modules.MarketData.Application.Ingestion;

/// <summary>Counts provider requests in one sync run and draws each from the shared <see cref="RateBudget"/>.</summary>
public sealed class RequestMeter(RateBudget budget)
{
    public int Count { get; private set; }

    /// <summary>Takes a token or throws <see cref="RateBudgetExhaustedException"/>.</summary>
    public void Take()
    {
        if (!budget.TryAcquire())
        {
            throw new RateBudgetExhaustedException();
        }

        Count++;
    }
}

/// <summary>Runs one sync and records its outcome in <c>market.provider_sync_state</c>.</summary>
public sealed class SyncRunner(SyncStateStore state, IMarketDataProvider provider, RateBudget budget, ILogger<SyncRunner> logger)
{
    /// <param name="rethrowUnavailable">True where §19.2 wants the job queue to retry (EOD bars); quote sync just waits for the next cycle.</param>
    public async Task RunAsync(string dataset, Func<RequestMeter, Task> work, bool rethrowUnavailable, CancellationToken cancellationToken)
    {
        var meter = new RequestMeter(budget);
        try
        {
            await work(meter);
            await state.RecordSuccessAsync(provider.Code, dataset, meter.Count, cancellationToken);
        }
        catch (RateBudgetExhaustedException)
        {
            logger.LogInformation("{Dataset} sync paused after {Requests} requests: rate budget exhausted", dataset, meter.Count);
            await state.RecordFailureAsync(provider.Code, dataset, meter.Count, SyncErrors.RateBudget, cancellationToken);
        }
        catch (MarketDataUnavailableException ex)
        {
            logger.LogWarning("{Dataset} sync failed: {Reason}", dataset, ex.Reason);
            await state.RecordFailureAsync(provider.Code, dataset, meter.Count, SyncErrors.ProviderUnavailable, cancellationToken);
            if (rethrowUnavailable)
            {
                throw;
            }
        }
    }
}
