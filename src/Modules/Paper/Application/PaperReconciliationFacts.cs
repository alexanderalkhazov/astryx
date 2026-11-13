using Astryx.Contracts.Paper;
using Astryx.Modules.Paper.Domain;
using Astryx.Modules.Paper.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Paper.Application;

/// <summary><see cref="IPaperReconciliationFacts"/>: fills, overdue orders and OCO legs, read across users (a system job).</summary>
public sealed class PaperReconciliationFacts(PaperDbContext db) : IPaperReconciliationFacts
{
    public async Task<IReadOnlyList<PositionFillTotals>> FillTotalsAsync(Guid portfolioId, CancellationToken cancellationToken)
    {
        var rows = await (
                from f in db.Fills.IgnoreQueryFilters()
                join o in db.Orders.IgnoreQueryFilters() on f.OrderId equals o.Id
                where f.PortfolioId == portfolioId && o.PositionId != null
                select new { PositionId = o.PositionId!.Value, f.Side, f.Quantity, f.Price, f.Fee })
            .AsNoTracking().ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.PositionId)
            .Select(g => new PositionFillTotals(
                g.Key,
                g.Where(r => r.Side == "buy").Sum(r => r.Quantity),
                g.Where(r => r.Side == "buy").Sum(r => r.Quantity * r.Price),
                g.Where(r => r.Side == "sell").Sum(r => r.Quantity),
                g.Where(r => r.Side == "sell").Sum(r => r.Quantity * r.Price),
                g.Sum(r => r.Fee)))
            .ToList();
    }

    public async Task<IReadOnlyList<Guid>> WorkingPastExpiryAsync(Guid portfolioId, DateTimeOffset before, CancellationToken cancellationToken) =>
        await db.Orders.IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.PortfolioId == portfolioId && o.Status == OrderStatus.Working && o.ExpiresAt != null && o.ExpiresAt < before)
            .Select(o => o.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, IReadOnlySet<string>>> WorkingExitsAsync(Guid portfolioId, CancellationToken cancellationToken)
    {
        var legs = await db.Orders.IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.PortfolioId == portfolioId && o.Status == OrderStatus.Working && o.PositionId != null && o.OcoGroupId != null)
            .Select(o => new { PositionId = o.PositionId!.Value, o.Purpose }).ToListAsync(cancellationToken);
        return legs.GroupBy(l => l.PositionId).ToDictionary(g => g.Key, g => (IReadOnlySet<string>)g.Select(l => l.Purpose).ToHashSet());
    }
}
