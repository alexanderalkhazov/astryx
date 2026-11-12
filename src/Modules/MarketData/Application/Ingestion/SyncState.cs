using Astryx.Modules.MarketData.Domain;
using Astryx.Modules.MarketData.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.MarketData.Application.Ingestion;

/// <summary>Dataset names in <c>market.provider_sync_state</c>.</summary>
public static class SyncDataset
{
    public const string Instruments = "instruments";
    public const string Calendar = "calendar";
    public const string EodBars = "eod_bars";
    public const string Quotes = "quotes";
    public const string Fundamentals = "fundamentals";
    public const string News = "news";
}

/// <summary>Reads and records sync outcomes per provider dataset (freshness, failure streaks, request counts).</summary>
public sealed class SyncStateStore(MarketDataDbContext db, MarketClock clock)
{
    public Task<ProviderSyncState?> GetAsync(string provider, string dataset, CancellationToken cancellationToken) =>
        db.ProviderSyncState.AsNoTracking().SingleOrDefaultAsync(s => s.Provider == provider && s.Dataset == dataset, cancellationToken);

    public Task RecordSuccessAsync(string provider, string dataset, int requests, CancellationToken cancellationToken) =>
        RecordAsync(provider, dataset, requests, errorCode: null, cancellationToken);

    public Task RecordFailureAsync(string provider, string dataset, int requests, string errorCode, CancellationToken cancellationToken) =>
        RecordAsync(provider, dataset, requests, errorCode, cancellationToken);

    private async Task RecordAsync(string provider, string dataset, int requests, string? errorCode, CancellationToken cancellationToken)
    {
        var now = clock.Now;
        var state = await db.ProviderSyncState.SingleOrDefaultAsync(s => s.Provider == provider && s.Dataset == dataset, cancellationToken);
        if (state is null)
        {
            state = new ProviderSyncState { Provider = provider, Dataset = dataset };
            db.ProviderSyncState.Add(state);
        }

        // The request counter restarts each UTC day.
        if (state.LastAttemptAt is { } last && last.UtcDateTime.Date != now.UtcDateTime.Date)
        {
            state.RequestsToday = 0;
        }

        state.LastAttemptAt = now;
        state.RequestsToday += requests;
        if (errorCode is null)
        {
            state.LastSuccessAt = now;
            state.LastErrorCode = null;
            state.ConsecutiveFailures = 0;
        }
        else
        {
            state.LastErrorCode = errorCode;
            state.ConsecutiveFailures++;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
