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
}
