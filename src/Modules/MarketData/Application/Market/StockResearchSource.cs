using Astryx.Contracts.MarketData;
using Astryx.Modules.MarketData.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.MarketData.Application.Market;

/// <summary>Stored quote, fundamentals and news for the AI context (SYSTEM_DESIGN.md §12.4). No provider calls.</summary>
public sealed class StockResearchSource(MarketDataDbContext db, MarketQueries queries, FreshnessPolicy freshness) : IStockResearchSource
{
    public async Task<ResearchQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken)
    {
        if (await queries.FindAsync(symbol, cancellationToken) is not { } instrument
            || await db.Quotes.AsNoTracking().SingleOrDefaultAsync(q => q.InstrumentId == instrument.Id, cancellationToken) is not { } quote)
        {
            return null;
        }

        var stale = (await freshness.QuoteAsync(quote.AsOf, cancellationToken)).Stale;
        return new ResearchQuote(quote.Last, quote.PrevClose, quote.Volume, quote.AsOf, quote.Quality, stale, quote.QuoteSeq, quote.Bid, quote.Ask);
    }

    public async Task<ResearchFundamentals?> GetFundamentalsAsync(string symbol, CancellationToken cancellationToken)
    {
        if (await queries.FindAsync(symbol, cancellationToken) is not { } instrument)
        {
            return null;
        }

        return await db.Fundamentals.AsNoTracking()
            .Where(f => f.InstrumentId == instrument.Id)
            .Select(f => new ResearchFundamentals(f.NextEarningsDate, f.PeRatio, f.Beta))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ResearchNewsItem>> GetNewsAsync(string symbol, DateTimeOffset since, DateTimeOffset until, int limit, CancellationToken cancellationToken)
    {
        if (await queries.FindAsync(symbol, cancellationToken) is not { } instrument)
        {
            return [];
        }

        return await db.NewsArticles.AsNoTracking()
            .Where(a => a.PublishedAt >= since && a.PublishedAt <= until && db.InstrumentNews.Any(n => n.ArticleId == a.Id && n.InstrumentId == instrument.Id))
            .OrderByDescending(a => a.PublishedAt).ThenBy(a => a.Id)
            .Take(limit)
            .Select(a => new ResearchNewsItem(a.PublishedAt, a.SourceName, a.Headline, a.ProviderSummary))
            .ToListAsync(cancellationToken);
    }
}
