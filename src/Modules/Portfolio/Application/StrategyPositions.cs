using Astryx.Contracts.Portfolio;
using Astryx.Modules.Portfolio.Domain;
using Astryx.Modules.Portfolio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Portfolio.Application;

/// <summary><see cref="IStrategyPositions"/>: open strategy positions joined to their portfolio's owner.</summary>
public sealed class StrategyPositions(PortfolioDbContext db) : IStrategyPositions
{
    public async Task<IReadOnlyList<StrategyPosition>> ListOpenAsync(CancellationToken cancellationToken) =>
        await db.Positions.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Status == PositionStatus.Open && p.StrategyVersionId != null)
            .Join(db.Portfolios.IgnoreQueryFilters(), p => p.PortfolioId, f => f.Id, (p, f) => new StrategyPosition(f.UserId, p.Id, p.Symbol, p.StrategyVersionId!.Value, p.OpenedAt))
            .ToListAsync(cancellationToken);
}
