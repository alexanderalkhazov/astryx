using Astryx.SharedKernel.Results;

namespace Astryx.Contracts.Portfolio;

/// <summary>
/// One cash movement to post (SYSTEM_DESIGN.md §10.4 <c>portfolio.transactions</c>). <paramref name="Amount"/> is signed
/// (debits negative) and already rounded to cents. <paramref name="IdempotencyRef"/> is unique across the ledger, e.g.
/// <c>fill:{fillId}:principal</c>.
/// </summary>
/// <param name="Type"><c>buy</c>, <c>sell</c>, <c>fee</c>, <c>reset_close</c>, <c>reset_deposit</c> or <c>adjustment</c>.</param>
public sealed record LedgerEntry(
    string Type, decimal Amount, string IdempotencyRef, string? Description = null, Guid? FillId = null, Guid? OrderId = null, Guid? PositionId = null);

/// <param name="Replayed">Every entry was already posted: nothing changed (a retried money movement).</param>
public sealed record LedgerPosting(decimal CashBalance, decimal ReservedCash, long Version, IReadOnlyList<Guid> TransactionIds, bool Replayed);

/// <summary>
/// The append-only cash ledger, owned by Portfolio and used by the paper execution engine through Contracts (§15.3).
/// Posts are atomic per call and serialised per portfolio (row lock). Callers check the portfolio belongs to the user.
/// </summary>
public interface IPortfolioLedger
{
    Task<Result<LedgerPosting>> PostAsync(Guid portfolioId, IReadOnlyList<LedgerEntry> entries, CancellationToken cancellationToken);

    /// <summary>Locks the portfolio row for the rest of the caller's transaction (§15.3 step 2). False if it doesn't exist.</summary>
    Task<bool> LockAsync(Guid portfolioId, CancellationToken cancellationToken);

    /// <summary>
    /// Changes <c>reserved_cash</c> by <paramref name="delta"/> (positive reserves for a working entry, negative
    /// releases it) and bumps the version. Refuses to reserve more than the cash (<c>insufficient_funds</c>).
    /// </summary>
    Task<Result<LedgerPosting>> ReserveAsync(Guid portfolioId, decimal delta, CancellationToken cancellationToken);
}

/// <summary>A filled entry becoming a position (§15.3 step 7).</summary>
public sealed record OpenPositionRequest(
    Guid PortfolioId, Guid InstrumentId, string Symbol, decimal Quantity, decimal AvgEntryPrice, decimal Fee, decimal Stop, decimal Target,
    Guid EntryOrderId, Guid ProposalId, Guid? StrategyVersionId, DateTimeOffset OpenedAt);

/// <summary>An open position as the paper engine needs it to exit.</summary>
public sealed record PositionSnapshot(
    Guid Id, Guid PortfolioId, Guid InstrumentId, string Symbol, string Status, decimal Quantity, decimal AvgEntryPrice, decimal FeesPaid, decimal? StopPrice,
    decimal? ExitPrice = null, string? ExitReason = null, decimal? RealizedPnl = null, DateTimeOffset? ClosedAt = null);

/// <summary>Owned by Portfolio; the paper engine is its only writer.</summary>
public interface IPositionBook
{
    /// <summary>Opens the position (one open per instrument; <c>conflict</c> if one exists) and returns its id.</summary>
    Task<Result<Guid>> OpenAsync(OpenPositionRequest request, CancellationToken cancellationToken);

    /// <summary>The position, locked for the caller's transaction, or null.</summary>
    Task<PositionSnapshot?> LockAsync(Guid positionId, CancellationToken cancellationToken);

    /// <summary>
    /// Closes an open position at <paramref name="exitPrice"/>: realized P&amp;L = qty × (exit − avg) − all fees, added to
    /// the portfolio's cumulative realized P&amp;L. Returns the realized P&amp;L; <c>position_not_open</c> if it isn't open.
    /// </summary>
    Task<Result<decimal>> CloseAsync(Guid positionId, decimal exitPrice, string exitReason, decimal exitFee, DateTimeOffset closedAt, CancellationToken cancellationToken);

    /// <summary>Records a stop-proximity alert for the session; false if one was already sent that session (§15.4).</summary>
    Task<bool> MarkStopAlertAsync(Guid positionId, DateOnly session, CancellationToken cancellationToken);
}

/// <summary>The outcome of starting a new performance epoch (P11).</summary>
/// <param name="Deposit">The <c>reset_deposit</c> posted to bring cash back to the starting cash (0 when already there).</param>
public sealed record EpochReset(decimal Deposit, decimal CashBalance, decimal StartingCash, int ResetCount, DateTimeOffset EpochStartedAt);

/// <summary>Owned by Portfolio; the paper reset is its only caller (SYSTEM_DESIGN.md §11.8 P11).</summary>
public interface IPortfolioEpochs
{
    /// <summary>
    /// With every position closed and every reservation released: posts <c>reset_deposit</c> back to the starting cash
    /// (ref <c>{resetRef}:deposit</c>), zeroes realized P&amp;L, sets start-of-day equity to the starting cash and starts a new
    /// epoch at <paramref name="at"/>. Joins the caller's transaction.
    /// </summary>
    Task<Result<EpochReset>> ResetAsync(Guid portfolioId, string resetRef, DateTimeOffset at, CancellationToken cancellationToken);
}
