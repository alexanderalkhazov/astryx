namespace Astryx.Contracts.MarketData;

/// <summary>The stored latest quote. <paramref name="Quality"/> is <c>realtime</c>, <c>delayed</c>, <c>eod</c> or <c>synthetic</c>.</summary>
/// <param name="Stale">Past its freshness budget (§20.5).</param>
public sealed record ResearchQuote(decimal Last, decimal PrevClose, long Volume, DateTimeOffset AsOf, string Quality, bool Stale);

public sealed record ResearchFundamentals(DateOnly? NextEarningsDate, decimal? PeRatio, decimal? Beta);

/// <summary>A stored news article mentioning the symbol, as the provider sent it (not sanitised).</summary>
public sealed record ResearchNewsItem(DateTimeOffset PublishedAt, string Source, string Headline, string? Summary);

/// <summary>
/// Stored quote, fundamentals and news for one symbol, owned by MarketData and read by Ai through Contracts
/// (SYSTEM_DESIGN.md §12.4). Never calls a provider: the AI context uses only what ingestion already validated.
/// </summary>
public interface IStockResearchSource
{
    Task<ResearchQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken);

    Task<ResearchFundamentals?> GetFundamentalsAsync(string symbol, CancellationToken cancellationToken);

    /// <summary>Newest first, published from <paramref name="since"/> through <paramref name="until"/>.</summary>
    Task<IReadOnlyList<ResearchNewsItem>> GetNewsAsync(string symbol, DateTimeOffset since, DateTimeOffset until, int limit, CancellationToken cancellationToken);
}
