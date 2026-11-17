using Astryx.Contracts.Portfolio;
using Astryx.Modules.Portfolio.Domain;
using Astryx.Modules.Portfolio.Infrastructure.Persistence;
using Astryx.SharedKernel;
using Astryx.SharedKernel.Finance;
using Microsoft.EntityFrameworkCore;
using PortfolioEntity = Astryx.Modules.Portfolio.Domain.Portfolio;

namespace Astryx.Modules.Portfolio.Application;

/// <summary>Which portfolio a user trades in: an attached Alpaca paper portfolio wins over the simulated one (§17.1).</summary>
public static class ActivePortfolioQuery
{
    public static IQueryable<PortfolioEntity> ActiveFor(this IQueryable<PortfolioEntity> portfolios, Guid userId) =>
        portfolios
            .Where(p => p.UserId == userId && (p.Mode == PortfolioMode.Simulated || (p.Mode == PortfolioMode.AlpacaPaper && p.BrokerConnectionId != null)))
            .OrderBy(p => p.Mode == PortfolioMode.Simulated ? 1 : 0);
}

public sealed class PortfolioDirectory(PortfolioDbContext db) : IPortfolioDirectory
{
    public async Task<Guid?> GetSimulatedAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Portfolios.IgnoreQueryFilters().Where(p => p.UserId == userId && p.Mode == PortfolioMode.Simulated).Select(p => (Guid?)p.Id).SingleOrDefaultAsync(cancellationToken);

    public async Task<ActivePortfolio?> GetActiveAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Portfolios.IgnoreQueryFilters().AsNoTracking().ActiveFor(userId)
            .Select(p => new ActivePortfolio(p.Id, p.Mode, p.BrokerConnectionId))
            .FirstOrDefaultAsync(cancellationToken);
}

/// <summary><see cref="IBrokerPortfolios"/>.</summary>
public sealed class BrokerPortfolios(PortfolioDbContext db, TimeProvider timeProvider) : IBrokerPortfolios
{
    public async Task<Guid> AttachAsync(Guid userId, Guid connectionId, decimal brokerCash, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var portfolio = await db.Portfolios.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.UserId == userId && p.Mode == PortfolioMode.AlpacaPaper, cancellationToken);
        if (portfolio is null)
        {
            var cash = RoundingPolicy.Ledger(brokerCash);
            portfolio = new PortfolioEntity
            {
                Id = Ids.New(timeProvider),
                UserId = userId,
                Mode = PortfolioMode.AlpacaPaper,
                StartingCash = cash,
                CashBalance = cash,
                StartOfDayEquity = cash,
                Version = 1,
                CreatedAt = at,
                UpdatedAt = at,
                EpochStartedAt = at,
            };
            db.Portfolios.Add(portfolio);
            db.Transactions.Add(new LedgerTransaction
            {
                Id = Ids.New(timeProvider),
                PortfolioId = portfolio.Id,
                Type = LedgerType.InitialDeposit,
                Amount = cash,
                BalanceAfter = cash,
                IdempotencyRef = $"initial_deposit:{portfolio.Id}",
                OccurredAt = at,
                Description = "Alpaca paper account balance when connected",
            });
        }

        portfolio.BrokerConnectionId = connectionId;
        portfolio.UpdatedAt = at;
        portfolio.Version++;
        await db.SaveChangesAsync(cancellationToken);
        return portfolio.Id;
    }

    public async Task DetachAsync(Guid userId, CancellationToken cancellationToken)
    {
        var portfolio = await db.Portfolios.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.UserId == userId && p.Mode == PortfolioMode.AlpacaPaper, cancellationToken);
        if (portfolio?.BrokerConnectionId is not null)
        {
            portfolio.BrokerConnectionId = null;
            portfolio.UpdatedAt = timeProvider.GetUtcNow();
            portfolio.Version++;
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
