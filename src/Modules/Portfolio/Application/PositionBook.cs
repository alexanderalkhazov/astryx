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
}
