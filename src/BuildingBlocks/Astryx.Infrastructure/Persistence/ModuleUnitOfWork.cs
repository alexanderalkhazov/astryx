using System.Data;
using Npgsql;

namespace Astryx.Infrastructure.Persistence;

/// <summary>
/// One database transaction across module contexts (SYSTEM_DESIGN.md §15.3, §20.8): once begun, every module
/// DbContext resolved in the same DI scope shares this connection and enlists in this transaction, so a money movement
/// (order, fill, position, ledger, outbox) commits or rolls back as a whole. Begin it before resolving any context.
/// </summary>
public sealed class ModuleUnitOfWork(NpgsqlDataSource dataSource) : IAsyncDisposable
{
    private bool _committed;

    public NpgsqlConnection? Connection { get; private set; }

    public NpgsqlTransaction? Transaction { get; private set; }

    public bool Active => Transaction is not null;

    public async Task BeginAsync(CancellationToken cancellationToken)
    {
        if (Active)
        {
            throw new InvalidOperationException("The unit of work has already begun.");
        }

        Connection = await dataSource.OpenConnectionAsync(cancellationToken);
        Transaction = await Connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        await (Transaction ?? throw new InvalidOperationException("The unit of work has not begun.")).CommitAsync(cancellationToken);
        _committed = true;
    }

    /// <summary>Rolls back unless committed. Safe to call twice (the owner and the DI scope both dispose it).</summary>
    public async ValueTask DisposeAsync()
    {
        if (Transaction is { } transaction)
        {
            Transaction = null;
            if (!_committed)
            {
                await transaction.RollbackAsync();
            }

            await transaction.DisposeAsync();
        }

        if (Connection is { } connection)
        {
            Connection = null;
            await connection.DisposeAsync();
        }
    }
}
