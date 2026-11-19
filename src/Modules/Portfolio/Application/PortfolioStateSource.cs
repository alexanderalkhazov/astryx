using Astryx.Contracts.Portfolio;
using Astryx.Modules.Portfolio.Domain;
using Astryx.Modules.Portfolio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Portfolio.Application;

/// <summary><see cref="IPortfolioStateSource"/>: cash, reservations, version and open positions in one read.</summary>
public sealed class PortfolioStateSource(PortfolioDbContext db) : IPortfolioStateSource
{
    public async Task<PortfolioState?> GetAsync(Guid portfolioId, CancellationToken cancellationToken)
    {
        if (await db.Portfolios.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(p => p.Id == portfolioId, cancellationToken) is not { } p)
        {
            return null;
        }

        var open = await db.Positions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.PortfolioId == portfolioId && x.Status == PositionStatus.Open)
            .Select(x => new OpenPositionView(x.InstrumentId, x.Symbol, x.Quantity, x.StopPrice, x.Id))
            .ToListAsync(cancellationToken);
        return new PortfolioState(p.Id, p.UserId, p.CashBalance, p.ReservedCash, p.Version, p.StartOfDayEquity, open, p.ReconciliationHaltedAt, p.BrokerConnectionId);
    }
}
