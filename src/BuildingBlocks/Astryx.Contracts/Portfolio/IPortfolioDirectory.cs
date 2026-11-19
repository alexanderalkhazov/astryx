namespace Astryx.Contracts.Portfolio;

/// <summary>Owned by Portfolio: which portfolio a user trades in.</summary>
public interface IPortfolioDirectory
{
    /// <summary>The user's simulated paper portfolio, or null before registration provisioned it.</summary>
    Task<Guid?> GetSimulatedAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// The portfolio the user trades in now (SYSTEM_DESIGN.md §17.1): their Alpaca paper portfolio while a connection is
    /// attached, otherwise the simulated one. Null before registration provisioned it.
    /// </summary>
    Task<ActivePortfolio?> GetActiveAsync(Guid userId, CancellationToken cancellationToken);
}

/// <param name="Mode"><c>simulated</c> or <c>alpaca_paper</c>.</param>
/// <param name="BrokerConnectionId">Set for a broker portfolio: the connection whose keys execute its orders.</param>
public sealed record ActivePortfolio(Guid Id, string Mode, Guid? BrokerConnectionId)
{
    public bool IsBroker => BrokerConnectionId is not null;
}

/// <summary>Owned by Portfolio: the Alpaca paper portfolio's lifecycle, driven by broker connections (T-141, §17.6).</summary>
public interface IBrokerPortfolios
{
    /// <summary>
    /// Attaches the user's <c>alpaca_paper</c> portfolio to <paramref name="connectionId"/>, creating it on first connect with
    /// the broker's cash as its opening balance. Returns the portfolio id. Joins the caller's transaction.
    /// </summary>
    Task<Guid> AttachAsync(Guid userId, Guid connectionId, decimal brokerCash, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>Detaches it: the user trades in the simulated portfolio again. History stays.</summary>
    Task DetachAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Halts new entries after the broker's positions stopped matching ours (§17.7, T-145): the same
    /// <c>reconciliation_halted_at</c> the nightly reconciliation sets, cleared by an operator. Exits still work.
    /// </summary>
    Task HaltAsync(Guid portfolioId, IReadOnlyList<string> issues, DateTimeOffset at, CancellationToken cancellationToken);
}

/// <summary>An open position as the risk engine sees it (valuation uses the latest quote, not this record).</summary>
public sealed record OpenPositionView(Guid InstrumentId, string Symbol, decimal Quantity, decimal? StopPrice, Guid PositionId = default);

/// <summary>
/// A portfolio's state for a risk assessment (§14.2): the cash ledger cache, what working orders reserved, the
/// concurrency version and start-of-day equity for the daily loss halt.
/// </summary>
/// <param name="ReconciliationHaltedAt">Set when nightly reconciliation found a mismatch (§20.13): new entries are blocked, exits allowed.</param>
public sealed record PortfolioState(
    Guid Id, Guid UserId, decimal CashBalance, decimal ReservedCash, long Version, decimal? StartOfDayEquity, IReadOnlyList<OpenPositionView> OpenPositions,
    DateTimeOffset? ReconciliationHaltedAt = null, Guid? BrokerConnectionId = null);

/// <summary>Owned by Portfolio; read by Trading (risk engine) through Contracts.</summary>
public interface IPortfolioStateSource
{
    Task<PortfolioState?> GetAsync(Guid portfolioId, CancellationToken cancellationToken);
}
