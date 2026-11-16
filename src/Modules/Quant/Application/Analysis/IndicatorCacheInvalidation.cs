using Astryx.Contracts.Events;
using Astryx.Contracts.Events.MarketData;
using Astryx.Modules.Quant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Quant.Application.Analysis;

/// <summary>
/// A new or changed corporate action re-adjusts the instrument's history (SYSTEM_DESIGN.md §20.7), so its cached
/// indicator snapshots, keyed only by the last bar and parameters, are dropped and recomputed on the next read.
/// </summary>
public sealed class IndicatorCacheInvalidation(QuantDbContext db) : IIntegrationEventHandler<CorporateActionRecorded>
{
    public Task HandleAsync(CorporateActionRecorded integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken) =>
        db.IndicatorSnapshots.IgnoreQueryFilters().Where(s => s.InstrumentId == integrationEvent.InstrumentId).ExecuteDeleteAsync(cancellationToken);
}
