namespace Astryx.Contracts.Portfolio;

/// <summary>Owned by Portfolio: which portfolio a user trades in.</summary>
public interface IPortfolioDirectory
{
    /// <summary>The user's simulated paper portfolio, or null before registration provisioned it.</summary>
    Task<Guid?> GetSimulatedAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>An open position as the risk engine sees it (valuation uses the latest quote, not this record).</summary>
public sealed record OpenPositionView(Guid InstrumentId, string Symbol, decimal Quantity, decimal? StopPrice);

/// <summary>
/// A portfolio's state for a risk assessment (§14.2): the cash ledger cache, what working orders reserved, the
/// concurrency version and start-of-day equity for the daily loss halt.
/// </summary>
public sealed record PortfolioState(Guid Id, Guid UserId, decimal CashBalance, decimal ReservedCash, long Version, decimal? StartOfDayEquity, IReadOnlyList<OpenPositionView> OpenPositions);

/// <summary>Owned by Portfolio; read by Trading (risk engine) through Contracts.</summary>
public interface IPortfolioStateSource
{
    Task<PortfolioState?> GetAsync(Guid portfolioId, CancellationToken cancellationToken);
}
