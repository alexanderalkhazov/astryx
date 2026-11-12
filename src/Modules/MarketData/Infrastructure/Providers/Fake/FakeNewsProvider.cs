using Astryx.Modules.MarketData.Application.Providers;

namespace Astryx.Modules.MarketData.Infrastructure.Providers.Fake;

/// <summary>Fixture headlines, including prompt-injection attempts for the AI layer's tests (SYSTEM_DESIGN.md §21.1).</summary>
internal sealed class FakeNewsProvider(FixtureData fixtures, TimeProvider timeProvider) : INewsProvider
{
    public Task<IReadOnlyList<ProviderNewsItem>> GetNewsAsync(string symbol, DateTimeOffset since, int limit, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        IReadOnlyList<ProviderNewsItem> items = fixtures.News
            .Where(n => n.Symbols.Contains(symbol, StringComparer.OrdinalIgnoreCase))
            .Select(n => new ProviderNewsItem(n.Id, n.Source, n.Headline, $"https://news.example.invalid/{n.Id}", now.AddMinutes(-n.MinutesAgo), n.Summary, n.Symbols))
            .Where(n => n.PublishedAt >= since)
            .OrderByDescending(n => n.PublishedAt)
            .Take(limit)
            .ToList();
        return Task.FromResult(items);
    }
}
