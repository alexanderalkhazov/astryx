using Astryx.Modules.MarketData.Application.Ingestion;
using Astryx.Modules.MarketData.Domain;
using Astryx.Modules.MarketData.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Astryx.Modules.MarketData.Application.Market;

/// <summary><c>MarketData:Freshness</c> budgets (SYSTEM_DESIGN.md §20.5).</summary>
public sealed class FreshnessOptions
{
    public const string SectionName = "MarketData:Freshness";

    /// <summary>A quote older than this while the regular session is open is stale (ASSUMPTION; tighter with real-time data).</summary>
    public int QuoteMaxAgeSeconds { get; set; } = 90;

    /// <summary>Daily bars must include the latest completed session by this hour, New York time.</summary>
    public int DailyBarsDeadlineHour { get; set; } = 18;
}

/// <summary>Every market-derived value goes out with when it was true and whether it is still within budget.</summary>
public sealed record Freshness(DateTimeOffset? AsOf, bool Stale);

/// <summary>Freshness of the market data as a whole, for <c>/system/status</c> and the connection-lost banner (§8.11).</summary>
/// <param name="FeedDown">
/// The latest quote sync could not reach the provider. Reported at once, before the quotes age past their budget, so
/// the banner appears within a sync cycle. Quote sync calls the provider only around the session, so a provider that
/// fails while the market is closed shows up at the next sync.
/// </param>
public sealed record MarketDataStatus(Freshness Quotes, DateOnly? DailyBarsThrough, bool DailyBarsStale, bool FeedDown);

/// <summary>
/// Applies the freshness budgets (SYSTEM_DESIGN.md §20.5). Stale data blocks approvals (<c>data_fresh</c>) and shows
/// the banner; callers attach <see cref="Freshness"/> to every response carrying market numbers.
/// </summary>
public sealed class FreshnessPolicy(MarketDataDbContext db, MarketClock clock, IOptions<FreshnessOptions> options)
{
    /// <summary>
    /// Session open: fresh if no older than <see cref="FreshnessOptions.QuoteMaxAgeSeconds"/>. Otherwise fresh if it
    /// reflects the most recent close ("at close"): a real feed stamps the last trade, a moment before the bell, so a
    /// quote within the same budget of the close counts.
    /// </summary>
    public async Task<Freshness> QuoteAsync(DateTimeOffset? asOf, CancellationToken cancellationToken)
    {
        if (asOf is null)
        {
            return new Freshness(null, Stale: true);
        }

        var session = await clock.CurrentSessionAsync(cancellationToken);
        return new Freshness(asOf, IsQuoteStale(asOf.Value, clock.Now, session.CloseAt!.Value, TimeSpan.FromSeconds(options.Value.QuoteMaxAgeSeconds)));
    }

    /// <summary>The rule behind <see cref="QuoteAsync"/>, for the current (or most recent) session's close.</summary>
    public static bool IsQuoteStale(DateTimeOffset asOf, DateTimeOffset now, DateTimeOffset closeAt, TimeSpan maxAge) =>
        now < closeAt ? now - asOf > maxAge : asOf < closeAt - maxAge;

    /// <summary>Fresh if the stored history includes every session whose deadline (18:00 ET on its date) has passed.</summary>
    public async Task<bool> DailyBarsStaleAsync(DateOnly? lastFinalSession, CancellationToken cancellationToken)
    {
        var required = await RequiredDailySessionAsync(cancellationToken);
        return required is not null && (lastFinalSession is null || lastFinalSession < required);
    }

    public async Task<MarketDataStatus> StatusAsync(CancellationToken cancellationToken)
    {
        var newestQuote = await db.Quotes.MaxAsync(q => (DateTimeOffset?)q.AsOf, cancellationToken);
        var barsThrough = await db.PriceBars.Where(b => b.Timeframe == Timeframe.OneDay && b.IsFinal).MaxAsync(b => (DateOnly?)b.SessionDate, cancellationToken);
        var quoteSync = await db.ProviderSyncState.AsNoTracking()
            .Where(s => s.Dataset == SyncDataset.Quotes)
            .OrderByDescending(s => s.LastAttemptAt)
            .FirstOrDefaultAsync(cancellationToken);
        var feedDown = quoteSync is { ConsecutiveFailures: > 0, LastErrorCode: SyncErrors.ProviderUnavailable };
        return new MarketDataStatus(await QuoteAsync(newestQuote, cancellationToken), barsThrough, await DailyBarsStaleAsync(barsThrough, cancellationToken), feedDown);
    }

    private async Task<DateOnly?> RequiredDailySessionAsync(CancellationToken cancellationToken)
    {
        for (var date = clock.Today; date >= clock.Today.AddDays(-14); date = date.AddDays(-1))
        {
            if (await clock.SessionAsync(date, cancellationToken) is { IsHoliday: false } session
                && MarketClock.At(session.SessionDate, options.Value.DailyBarsDeadlineHour, 0) <= clock.Now)
            {
                return session.SessionDate;
            }
        }

        return null;
    }
}
