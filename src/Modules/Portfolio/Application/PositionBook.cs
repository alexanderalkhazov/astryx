using Astryx.Contracts.Portfolio;
using Astryx.Modules.Portfolio.Domain;
using Astryx.Modules.Portfolio.Infrastructure.Persistence;
using Astryx.SharedKernel;
using Astryx.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Astryx.Modules.Portfolio.Application;

/// <summary><see cref="IPositionBook"/>: positions are opened only by the paper engine, inside its transaction (§15.3).</summary>
public sealed class PositionBook(PortfolioDbContext db, TimeProvider timeProvider) : IPositionBook
{
    public static readonly Error AlreadyOpen = new("conflict", "A position in this instrument is already open.");

    public async Task<Result<Guid>> OpenAsync(OpenPositionRequest r, CancellationToken cancellationToken)
    {
        var position = new Position
        {
            Id = Ids.New(timeProvider),
            PortfolioId = r.PortfolioId,
            InstrumentId = r.InstrumentId,
            Symbol = r.Symbol,
            Quantity = r.Quantity,
            AvgEntryPrice = r.AvgEntryPrice,
            CostBasis = r.Quantity * r.AvgEntryPrice,
            StopPrice = r.Stop,
            TargetPrice = r.Target,
            EntryOrderId = r.EntryOrderId,
            ProposalId = r.ProposalId,
            StrategyVersionId = r.StrategyVersionId,
            OpenedAt = r.OpenedAt,
            FeesPaid = r.Fee,
            Version = 1,
        };
        db.Positions.Add(position);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(position).State = EntityState.Detached;
            return AlreadyOpen;
        }

        return position.Id;
    }

    public static readonly Error NotOpen = new("position_not_open", "This position is no longer open.");

    public async Task<PositionSnapshot?> LockAsync(Guid positionId, CancellationToken cancellationToken) =>
        (await db.Positions.FromSql($"SELECT * FROM portfolio.positions WHERE id = {positionId} FOR UPDATE").IgnoreQueryFilters().AsNoTracking().ToListAsync(cancellationToken))
            .Select(p => new PositionSnapshot(p.Id, p.PortfolioId, p.InstrumentId, p.Symbol, p.Status, p.Quantity, p.AvgEntryPrice, p.FeesPaid, p.StopPrice, p.ExitPrice, p.ExitReason, p.RealizedPnl, p.ClosedAt))
            .FirstOrDefault();

    public async Task<Result<decimal>> CloseAsync(Guid positionId, decimal exitPrice, string exitReason, decimal exitFee, DateTimeOffset closedAt, CancellationToken cancellationToken)
    {
        var position = await db.Positions.IgnoreQueryFilters().SingleOrDefaultAsync(p => p.Id == positionId, cancellationToken);
        if (position is not { Status: PositionStatus.Open })
        {
            return NotOpen;
        }

        var fees = position.FeesPaid + exitFee;
        var realized = Astryx.SharedKernel.Finance.RoundingPolicy.Ledger((position.Quantity * (exitPrice - position.AvgEntryPrice)) - fees);
        position.Status = PositionStatus.Closed;
        position.ExitPrice = exitPrice;
        position.ExitReason = exitReason;
        position.ClosedAt = closedAt;
        position.FeesPaid = fees;
        position.RealizedPnl = realized;
        position.Version++;
        await db.SaveChangesAsync(cancellationToken);
        await db.Portfolios.IgnoreQueryFilters().Where(p => p.Id == position.PortfolioId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.RealizedPnl, p => p.RealizedPnl + realized), cancellationToken);
        return realized;
    }

    public async Task<bool> MarkStopAlertAsync(Guid positionId, DateOnly session, CancellationToken cancellationToken) =>
        await db.Positions.IgnoreQueryFilters()
            .Where(p => p.Id == positionId && p.Status == PositionStatus.Open && (p.StopAlertSession == null || p.StopAlertSession < session))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.StopAlertSession, session), cancellationToken) == 1;
}
