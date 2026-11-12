using System.Text.Json;
using Astryx.Infrastructure.Jobs;
using Astryx.Modules.MarketData.Application.Providers;
using Astryx.Modules.MarketData.Domain;
using Astryx.Modules.MarketData.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.MarketData.Application.Ingestion;

public sealed record InstrumentSyncJob : IJob
{
    public static string JobType => "market.instrument_sync.v1";
}

/// <summary>
/// Daily at 06:00 ET (or at once on an empty database): upserts the universe into the instrument master by
/// <c>(symbol, exchange_mic)</c>; instruments the provider no longer lists become inactive (SYSTEM_DESIGN.md §16.3, §16.4).
/// </summary>
public sealed class InstrumentSyncHandler(MarketDataDbContext db, IMarketDataProvider provider, SyncStateStore state, SyncRunner runner, MarketClock clock)
    : IJobHandler<InstrumentSyncJob>
{
    public const string Universe = "us_large_cap";

    public async Task HandleAsync(InstrumentSyncJob job, JobContext context, CancellationToken cancellationToken)
    {
        var last = await state.GetAsync(provider.Code, SyncDataset.Instruments, cancellationToken);
        if (last?.LastSuccessAt >= clock.LatestDaily(6, 0))
        {
            return;
        }

        await runner.RunAsync(SyncDataset.Instruments, meter => SyncAsync(meter, cancellationToken), rethrowUnavailable: false, cancellationToken);
    }

    private async Task SyncAsync(RequestMeter meter, CancellationToken cancellationToken)
    {
        meter.Take();
        var listed = await provider.GetInstrumentsAsync(Universe, cancellationToken);
        if (listed.Count == 0)
        {
            return; // an empty answer is more likely a provider fault than a delisted universe
        }

        var now = clock.Now;
        var existing = await db.Instruments.ToDictionaryAsync(i => (i.Symbol, i.ExchangeMic), cancellationToken);
        var seen = new HashSet<(string, string)>();
        foreach (var item in listed)
        {
            var key = (item.Symbol.ToUpperInvariant(), item.ExchangeMic);
            seen.Add(key);
            if (!existing.TryGetValue(key, out var instrument))
            {
                instrument = new Instrument { Id = Guid.CreateVersion7(now), Symbol = key.Item1, ExchangeMic = item.ExchangeMic, Name = item.Name };
                db.Instruments.Add(instrument);
            }

            instrument.Name = item.Name;
            instrument.AssetType = item.AssetType;
            instrument.Currency = item.Currency;
            instrument.Sector = item.Sector;
            instrument.Industry = item.Industry;
            instrument.MarketCap = item.MarketCap;
            instrument.IsActive = true;
            instrument.ProviderRefs = JsonSerializer.Serialize(new Dictionary<string, string> { [provider.Code] = item.ProviderId });
            instrument.UpdatedAt = now;
        }

        foreach (var (key, instrument) in existing)
        {
            if (!seen.Contains(key) && instrument.IsActive)
            {
                instrument.IsActive = false;
                instrument.UpdatedAt = now;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
