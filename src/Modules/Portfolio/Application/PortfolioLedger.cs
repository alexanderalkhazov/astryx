using Astryx.Contracts.Portfolio;
using Astryx.Modules.Portfolio.Domain;
using Astryx.Modules.Portfolio.Infrastructure.Persistence;
using Astryx.SharedKernel;
using Astryx.SharedKernel.Finance;
using Astryx.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Portfolio.Application;

/// <summary>
/// <see cref="IPortfolioLedger"/> (SYSTEM_DESIGN.md §10.4, §15.3, §20.8): locks the portfolio row, replays entries whose
/// <c>idempotency_ref</c> is already posted, appends the rest with a running <c>balance_after</c>, and updates the cash
/// cache and <c>version</c>, all in one transaction (joining the caller's, if any). Cash may never fall below what
/// working orders have reserved.
/// </summary>
public sealed class PortfolioLedger(PortfolioDbContext db, TimeProvider timeProvider) : IPortfolioLedger
{
    public static readonly Error NotFound = new("not_found", "No portfolio with that id.");
    public static readonly Error InsufficientFunds = new("insufficient_funds", "Not enough unreserved cash for this movement.");
    public static readonly Error PartialReplay = new("conflict", "Some of these ledger entries were already posted and some were not.");

    private static readonly HashSet<string> PostableTypes =
        [LedgerType.Buy, LedgerType.Sell, LedgerType.Fee, LedgerType.ResetClose, LedgerType.ResetDeposit, LedgerType.Adjustment];

    public async Task<Result<LedgerPosting>> PostAsync(Guid portfolioId, IReadOnlyList<LedgerEntry> entries, CancellationToken cancellationToken)
    {
        if (Validate(entries) is { } invalid)
        {
            return invalid;
        }

        var own = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            var portfolio = (await db.Portfolios.FromSql($"SELECT * FROM portfolio.portfolios WHERE id = {portfolioId} FOR UPDATE")
                .IgnoreQueryFilters().ToListAsync(cancellationToken)).FirstOrDefault();
            if (portfolio is null)
            {
                return NotFound;
            }

            var refs = entries.Select(e => e.IdempotencyRef).ToList();
            var posted = await db.Transactions.IgnoreQueryFilters()
                .Where(t => refs.Contains(t.IdempotencyRef))
                .Select(t => new { t.Id, t.IdempotencyRef, t.PortfolioId })
                .ToListAsync(cancellationToken);
            if (posted.Count == entries.Count && posted.All(p => p.PortfolioId == portfolioId))
            {
                var ids = refs.Select(r => posted.First(p => p.IdempotencyRef == r).Id).ToList();
                return new LedgerPosting(portfolio.CashBalance, portfolio.ReservedCash, portfolio.Version, ids, Replayed: true);
            }

            if (posted.Count > 0)
            {
                return PartialReplay;
            }

            var now = timeProvider.GetUtcNow();
            var balance = portfolio.CashBalance + entries.Sum(e => e.Amount);
            if (balance < portfolio.ReservedCash)
            {
                return InsufficientFunds;
            }

            // One insert per entry, in posting order: EF orders a batch by key, and `seq` must follow the posting.
            var running = portfolio.CashBalance;
            var created = new List<Guid>(entries.Count);
            foreach (var entry in entries)
            {
                running += entry.Amount;
                var row = new LedgerTransaction
                {
                    Id = Ids.New(timeProvider),
                    PortfolioId = portfolioId,
                    Type = entry.Type,
                    Amount = entry.Amount,
                    Currency = portfolio.BaseCurrency,
                    BalanceAfter = running,
                    FillId = entry.FillId,
                    OrderId = entry.OrderId,
                    PositionId = entry.PositionId,
                    IdempotencyRef = entry.IdempotencyRef,
                    OccurredAt = now,
                    Description = entry.Description,
                };
                db.Transactions.Add(row);
                await db.SaveChangesAsync(cancellationToken);
                created.Add(row.Id);
            }

            portfolio.CashBalance = balance;
            portfolio.Version++;
            portfolio.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            if (own is not null)
            {
                await own.CommitAsync(cancellationToken);
            }

            return new LedgerPosting(portfolio.CashBalance, portfolio.ReservedCash, portfolio.Version, created, Replayed: false);
        }
        finally
        {
            if (own is not null)
            {
                await own.DisposeAsync();
            }
        }
    }

    private static Error? Validate(IReadOnlyList<LedgerEntry> entries)
    {
        if (entries.Count == 0)
        {
            return Error.Validation("Post at least one entry.");
        }

        foreach (var e in entries)
        {
            if (!PostableTypes.Contains(e.Type))
            {
                return Error.Validation($"Ledger type '{e.Type}' can't be posted.");
            }

            if (e.Amount == 0 || e.Amount != RoundingPolicy.Ledger(e.Amount))
            {
                return Error.Validation($"{e.IdempotencyRef}: the amount must be non-zero and rounded to cents.");
            }

            if (string.IsNullOrWhiteSpace(e.IdempotencyRef) || e.IdempotencyRef.Length > 200)
            {
                return Error.Validation("Every entry needs an idempotency reference of at most 200 characters.");
            }
        }

        return entries.Select(e => e.IdempotencyRef).Distinct(StringComparer.Ordinal).Count() == entries.Count
            ? null
            : Error.Validation("Idempotency references must be distinct within a posting.");
    }
}

/// <summary>
/// The §20.8 ledger invariants for one portfolio: the cash cache equals the sum of the ledger, every
/// <c>balance_after</c> is the running sum, and reserved cash fits inside cash. Tests assert it after every scenario.
/// </summary>
public static class LedgerInvariants
{
    public static async Task<IReadOnlyList<string>> CheckAsync(PortfolioDbContext db, Guid portfolioId, CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        var portfolio = await db.Portfolios.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(p => p.Id == portfolioId, cancellationToken);
        if (portfolio is null)
        {
            return [$"portfolio {portfolioId} does not exist"];
        }

        var rows = await db.Transactions.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.PortfolioId == portfolioId).OrderBy(t => t.Seq)
            .Select(t => new { t.IdempotencyRef, t.Amount, t.BalanceAfter })
            .ToListAsync(cancellationToken);
        var running = 0m;
        foreach (var row in rows)
        {
            running += row.Amount;
            if (row.BalanceAfter != running)
            {
                violations.Add($"{row.IdempotencyRef}: balance_after {row.BalanceAfter} != running sum {running}");
            }
        }

        if (portfolio.CashBalance != running)
        {
            violations.Add($"cash_balance {portfolio.CashBalance} != ledger sum {running}");
        }

        if (portfolio.ReservedCash < 0 || portfolio.ReservedCash > portfolio.CashBalance)
        {
            violations.Add($"reserved_cash {portfolio.ReservedCash} is outside 0..cash_balance");
        }

        return violations;
    }
}
