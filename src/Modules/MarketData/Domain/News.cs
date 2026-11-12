namespace Astryx.Modules.MarketData.Domain;

/// <summary>Row in <c>market.news_articles</c>, deduplicated per provider (SYSTEM_DESIGN.md §10.4).</summary>
public sealed class NewsArticle
{
    public const int MaxHeadlineLength = 500;

    public Guid Id { get; set; }

    public required string Provider { get; set; }

    public required string ProviderArticleId { get; set; }

    public required string SourceName { get; set; }

    public required string Headline { get; set; }

    public required string Url { get; set; }

    public DateTimeOffset PublishedAt { get; set; }

    public string? ProviderSummary { get; set; }

    public DateTimeOffset IngestedAt { get; set; }
}

/// <summary>Row in <c>market.instrument_news</c>: which instruments an article mentions.</summary>
public sealed class InstrumentNews
{
    public Guid ArticleId { get; set; }

    public Guid InstrumentId { get; set; }
}
