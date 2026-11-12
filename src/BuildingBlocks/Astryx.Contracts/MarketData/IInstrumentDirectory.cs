namespace Astryx.Contracts.MarketData;

/// <summary>An instrument with its latest price, for lists such as search results, recents and watchlists.</summary>
/// <param name="Last">Latest stored quote; null before the first quote sync.</param>
/// <param name="ChangePct">Fraction from the previous close (0.0342 = 3.42 %).</param>
/// <param name="MarketCap">USD; null when the provider has none.</param>
public sealed record InstrumentSummary(Guid Id, string Symbol, string Name, string? Sector, decimal? Last, decimal? ChangePct, DateTimeOffset? AsOf, decimal? MarketCap = null, string? Industry = null);

/// <summary>Instrument lookup owned by MarketData and used by other modules (search, watchlists) through Contracts.</summary>
public interface IInstrumentDirectory
{
    /// <summary>Exact ticker, then ticker prefix (largest first), then company-name matches.</summary>
    Task<IReadOnlyList<InstrumentSummary>> SearchAsync(string query, int limit, CancellationToken cancellationToken);

    Task<InstrumentSummary?> FindAsync(string symbol, CancellationToken cancellationToken);

    /// <summary>Summaries for the given ids; unknown ids are skipped. Order is not guaranteed.</summary>
    Task<IReadOnlyList<InstrumentSummary>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Every active equity, by symbol (the scanner universe before filters).</summary>
    Task<IReadOnlyList<InstrumentSummary>> ListActiveEquitiesAsync(CancellationToken cancellationToken);
}
