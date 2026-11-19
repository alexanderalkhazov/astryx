using System.Text.Json;
using Astryx.Contracts.Portfolio;
using Astryx.Infrastructure.Observability;
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
public sealed class BrokerPortfolios(PortfolioDbContext db, IPortfolioLedger ledger, TimeProvider timeProvider) : IBrokerPortfolios
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
        else if (await ReopenAsync(portfolio, brokerCash, at, cancellationToken))
        {
            return await AttachedAsync(portfolio.Id, connectionId, at, cancellationToken);
        }

        portfolio.BrokerConnectionId = connectionId;
        portfolio.UpdatedAt = at;
        portfolio.Version++;
        await db.SaveChangesAsync(cancellationToken);
        return portfolio.Id;
    }

    /// <summary>
    /// A reconnect may be a different Alpaca account (new keys, a reset paper account), so a flat portfolio starts a new
    /// epoch at the broker's cash, as a paper reset does (PAPER-08): the difference is a deposit, not profit. A portfolio
    /// still holding positions keeps its epoch, and reconciliation reports any mismatch (§17.7).
    /// </summary>
    private async Task<bool> ReopenAsync(PortfolioEntity portfolio, decimal brokerCash, DateTimeOffset at, CancellationToken cancellationToken)
    {
        if (!await ledger.LockAsync(portfolio.Id, cancellationToken))
        {
            return false;
        }

        var current = await db.Portfolios.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == portfolio.Id, cancellationToken);
        if (current.ReservedCash != 0 || await db.Positions.IgnoreQueryFilters().AnyAsync(p => p.PortfolioId == portfolio.Id && p.Status == PositionStatus.Open, cancellationToken))
        {
            return false;
        }

        db.Entry(portfolio).State = EntityState.Detached; // the ledger and the update below write the row directly
        var cash = RoundingPolicy.Ledger(brokerCash);
        if (cash != current.CashBalance)
        {
            var posted = await ledger.PostAsync(portfolio.Id, [new LedgerEntry(LedgerType.ResetDeposit, cash - current.CashBalance, $"reconnect:{portfolio.Id}:{at.ToUnixTimeMilliseconds()}", "Alpaca account reconnected: cash set to the account balance")], cancellationToken);
            if (!posted.IsSuccess)
            {
                throw new InvalidOperationException(posted.Error.Message);
            }
        }

        await db.Portfolios.IgnoreQueryFilters().Where(p => p.Id == portfolio.Id).ExecuteUpdateAsync(
            s => s.SetProperty(p => p.StartingCash, cash)
                .SetProperty(p => p.RealizedPnl, 0m)
                .SetProperty(p => p.StartOfDayEquity, cash)
                .SetProperty(p => p.ResetCount, p => p.ResetCount + 1)
                .SetProperty(p => p.EpochStartedAt, at),
            cancellationToken);
        return true;
    }

    private async Task<Guid> AttachedAsync(Guid portfolioId, Guid connectionId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await db.Portfolios.IgnoreQueryFilters().Where(p => p.Id == portfolioId).ExecuteUpdateAsync(
            s => s.SetProperty(p => p.BrokerConnectionId, connectionId)
                .SetProperty(p => p.UpdatedAt, at)
                .SetProperty(p => p.Version, p => p.Version + 1),
            cancellationToken);
        return portfolioId;
    }

    public async Task HaltAsync(Guid portfolioId, IReadOnlyList<string> issues, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var halted = await db.Portfolios.IgnoreQueryFilters().Where(p => p.Id == portfolioId && p.ReconciliationHaltedAt == null).ExecuteUpdateAsync(
            s => s.SetProperty(p => p.ReconciliationHaltedAt, at).SetProperty(p => p.ReconciliationIssues, JsonSerializer.Serialize(issues)),
            cancellationToken);
        if (halted > 0)
        {
            AstryxTelemetry.ReconciliationMismatches.Add(issues.Count, new KeyValuePair<string, object?>("kind", "broker_position"));
        }
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
