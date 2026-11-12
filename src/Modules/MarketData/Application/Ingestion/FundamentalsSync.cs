using System.Text.Json;
using Astryx.Infrastructure.Jobs;
using Astryx.Modules.MarketData.Application.Providers;
using Astryx.Modules.MarketData.Domain;
using Astryx.Modules.MarketData.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.MarketData.Application.Ingestion;

public sealed record FundamentalsSyncJob : IJob
{
    public static string JobType => "market.fundamentals_sync.v1";
}

/// <summary>Daily at 07:00 ET: the latest fundamentals for every active equity (SYSTEM_DESIGN.md §16.3).</summary>
public sealed class FundamentalsSyncHandler(MarketDataDbContext db, IMarketDataProvider provider, SyncStateStore state, SyncRunner runner, MarketClock clock)
    : IJobHandler<FundamentalsSyncJob>
{
    public async Task HandleAsync(FundamentalsSyncJob job, JobContext context, CancellationToken cancellationToken)
    {
        if (!provider.Capabilities.Fundamentals || !await db.Instruments.AnyAsync(i => i.IsActive, cancellationToken))
        {
            return; // nothing to fetch yet; don't mark today done before the instrument sync
        }

        var last = await state.GetAsync(provider.Code, SyncDataset.Fundamentals, cancellationToken);
        if (last?.LastErrorCode is null && last?.LastSuccessAt >= clock.LatestDaily(7, 0))
        {
            return;
        }

        await runner.RunAsync(SyncDataset.Fundamentals, meter => SyncAsync(meter, cancellationToken), rethrowUnavailable: false, cancellationToken);
    }

    private async Task SyncAsync(RequestMeter meter, CancellationToken cancellationToken)
    {
        var refreshedBefore = clock.LatestDaily(7, 0);
        var due = await db.Instruments
            .Where(i => i.IsActive && i.AssetType == AssetType.Equity)
            .Where(i => !db.Fundamentals.Any(f => f.InstrumentId == i.Id && f.AsOf >= refreshedBefore))
            .OrderBy(i => i.Symbol)
            .Select(i => new { i.Id, i.Symbol })
            .ToListAsync(cancellationToken);
        var existing = await db.Fundamentals.ToDictionaryAsync(f => f.InstrumentId, cancellationToken);

        try
        {
            foreach (var instrument in due)
            {
                meter.Take();
                if (await provider.GetFundamentalsAsync(instrument.Symbol, cancellationToken) is not { } item)
                {
                    continue;
                }

                if (!existing.TryGetValue(instrument.Id, out var row))
                {
                    row = new Fundamentals { InstrumentId = instrument.Id, Source = provider.Code };
                    db.Fundamentals.Add(row);
                }

                row.AsOf = item.AsOf;
                row.Beta = item.Beta;
                row.PeRatio = item.PeRatio;
                row.EpsTtm = item.EpsTtm;
                row.NextEarningsDate = item.NextEarningsDate;
                row.Data = JsonSerializer.Serialize(item.Data);
                row.Source = provider.Code;
            }
        }
        finally
        {
            await db.SaveChangesAsync(cancellationToken); // keep progress if the budget runs out part-way
        }
    }
}
