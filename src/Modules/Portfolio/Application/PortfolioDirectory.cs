using Astryx.Contracts.Portfolio;
using Astryx.Modules.Portfolio.Domain;
using Astryx.Modules.Portfolio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Portfolio.Application;

public sealed class PortfolioDirectory(PortfolioDbContext db) : IPortfolioDirectory
{
    public async Task<Guid?> GetSimulatedAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Portfolios.IgnoreQueryFilters().Where(p => p.UserId == userId && p.Mode == PortfolioMode.Simulated).Select(p => (Guid?)p.Id).SingleOrDefaultAsync(cancellationToken);
}
