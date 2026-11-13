using Astryx.Contracts.Portfolio;
using Astryx.Modules.Portfolio.Domain;
using Astryx.Modules.Portfolio.Infrastructure.Persistence;
using Astryx.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Portfolio.Application;

/// <summary><see cref="IPortfolioEpochs"/>: the Portfolio half of a paper reset (SYSTEM_DESIGN.md §11.8 P11, PAPER-08).</summary>
public sealed class PortfolioEpochs(PortfolioDbContext db, IPortfolioLedger ledger) : IPortfolioEpochs
{
    public static readonly Error NotFlat = new("conflict", "Close every position and release every reservation before starting a new epoch.");

    public async Task<Result<EpochReset>> ResetAsync(Guid portfolioId, string resetRef, DateTimeOffset at, CancellationToken cancellationToken)
    {
        if (!await ledger.LockAsync(portfolioId, cancellationToken))
        {
            return PortfolioLedger.NotFound;
        }

        var portfolio = await db.Portfolios.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == portfolioId, cancellationToken);
        if (portfolio.ReservedCash != 0 || await db.Positions.IgnoreQueryFilters().AnyAsync(p => p.PortfolioId == portfolioId && p.Status == PositionStatus.Open, cancellationToken))
        {
            return NotFlat;
        }

        var deposit = portfolio.StartingCash - portfolio.CashBalance;
        if (deposit != 0)
        {
            var posted = await ledger.PostAsync(portfolioId, [new LedgerEntry(LedgerType.ResetDeposit, deposit, $"{resetRef}:deposit", "Paper account reset: cash back to the starting balance")], cancellationToken);
            if (!posted.IsSuccess)
            {
                return posted.Error;
            }
        }

        await db.Portfolios.IgnoreQueryFilters().Where(p => p.Id == portfolioId).ExecuteUpdateAsync(
            s => s.SetProperty(p => p.RealizedPnl, 0m)
                .SetProperty(p => p.StartOfDayEquity, p => p.StartingCash)
                .SetProperty(p => p.ResetCount, p => p.ResetCount + 1)
                .SetProperty(p => p.EpochStartedAt, at)
                .SetProperty(p => p.UpdatedAt, at)
                .SetProperty(p => p.Version, p => p.Version + 1),
            cancellationToken);
        return new EpochReset(deposit, portfolio.StartingCash, portfolio.StartingCash, portfolio.ResetCount + 1, at);
    }
}
