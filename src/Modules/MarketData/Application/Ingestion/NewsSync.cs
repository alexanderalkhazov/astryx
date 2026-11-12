using Astryx.Contracts.MarketData;
using Astryx.Infrastructure.Jobs;
using Astryx.Modules.MarketData.Application.Providers;
using Astryx.Modules.MarketData.Domain;
using Astryx.Modules.MarketData.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.MarketData.Application.Ingestion;

public sealed record NewsSyncJob : IJob
{
    public static string JobType => "market.news_sync.v1";
}

/// <summary>
/// Stores headlines for one symbol, deduplicated per provider article id and linked to every instrument it mentions.
/// Used by <see cref="NewsSyncHandler"/> and on demand before an analysis (SYSTEM_DESIGN.md §16.3).
/// </summary>
public sealed class NewsIngestor(MarketDataDbContext db, INewsProvider news, IMarketDataProvider provider, MarketClock clock)
{
    public async Task<int> IngestAsync(string symbol, CancellationToken cancellationToken)
    {
        var items = await news.GetNewsAsync(symbol, clock.Now.AddDays(-7), 50, cancellationToken);
        if (items.Count == 0)
        {
            return 0;
        }

        var ids = items.Select(i => i.ProviderArticleId).ToList();
        var stored = await db.NewsArticles.Where(a => a.Provider == provider.Code && ids.Contains(a.ProviderArticleId)).ToDictionaryAsync(a => a.ProviderArticleId, cancellationToken);
        var mentioned = items.SelectMany(i => i.Symbols).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var instruments = await db.Instruments.Where(i => mentioned.Contains(i.Symbol)).ToDictionaryAsync(i => i.Symbol, i => i.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var added = 0;
        foreach (var item in items.Where(i => !stored.ContainsKey(i.ProviderArticleId)))
        {
            var article = new NewsArticle
            {
                Id = Guid.CreateVersion7(clock.Now),
                Provider = provider.Code,
                ProviderArticleId = item.ProviderArticleId,
                SourceName = item.SourceName,
                Headline = item.Headline.Length > NewsArticle.MaxHeadlineLength ? item.Headline[..NewsArticle.MaxHeadlineLength] : item.Headline,
                Url = item.Url,
                PublishedAt = item.PublishedAt,
                ProviderSummary = item.Summary,
                IngestedAt = clock.Now,
            };
            db.NewsArticles.Add(article);
            foreach (var mention in item.Symbols.Where(instruments.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                db.InstrumentNews.Add(new InstrumentNews { ArticleId = article.Id, InstrumentId = instruments[mention] });
            }

            added++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return added;
    }
}

/// <summary>Every 15 minutes: headlines for the priority symbols (held, working orders, watchlists, signals).</summary>
public sealed class NewsSyncHandler(NewsIngestor ingestor, IEnumerable<IPrioritySymbolSource> prioritySources, SyncRunner runner)
    : IJobHandler<NewsSyncJob>
{
    public Task HandleAsync(NewsSyncJob job, JobContext context, CancellationToken cancellationToken) =>
        runner.RunAsync(SyncDataset.News, async meter =>
        {
            var symbols = new List<string>();
            foreach (var source in prioritySources.OrderBy(s => s.Priority))
            {
                symbols.AddRange(await source.GetSymbolsAsync(cancellationToken));
            }

            foreach (var symbol in symbols.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                meter.Take();
                await ingestor.IngestAsync(symbol, cancellationToken);
            }
        }, rethrowUnavailable: false, cancellationToken);
}
