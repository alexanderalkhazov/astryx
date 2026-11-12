using Astryx.Contracts.MarketData;
using Astryx.Infrastructure.Security;
using Astryx.Modules.Discovery.Domain;
using Astryx.Modules.Discovery.Infrastructure.Persistence;
using Astryx.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Discovery.Application;

/// <summary>A search result or recent (SYSTEM_DESIGN.md §11.4 InstrumentSearchItem).</summary>
/// <param name="Held">Open position: filled once Portfolio exists (M5).</param>
/// <param name="SignalType">Active signal badge: filled once the scanner exists (M3).</param>
public sealed record InstrumentSearchItem(string Symbol, string Name, string? Sector, decimal? Last, decimal? ChangePct, bool Held, string? SignalType);

/// <summary>Instrument search and the user's recent searches (SYSTEM_DESIGN.md §8.4.12, §11.4 M1–M3).</summary>
public sealed class SearchService(DiscoveryDbContext db, IInstrumentDirectory instruments, ICurrentUser currentUser, TimeProvider timeProvider)
{
    public const int MaxQueryLength = 40;
    public const int MaxLimit = 50;

    public static readonly Error InstrumentNotFound = new("instrument_not_found", "No instrument with that symbol.");

    public async Task<Result<IReadOnlyList<InstrumentSearchItem>>> SearchAsync(string? query, int limit, CancellationToken cancellationToken)
    {
        var text = query?.Trim() ?? string.Empty;
        if (text.Length is 0 or > MaxQueryLength || limit is < 1 or > MaxLimit)
        {
            return new Error("bad_request", $"q must be 1–{MaxQueryLength} characters and limit 1–{MaxLimit}.");
        }

        var found = await instruments.SearchAsync(text, limit, cancellationToken);
        return found.Select(ToItem).ToList();
    }

    /// <summary>The newest recents first (at most <see cref="RecentSearch.Keep"/>).</summary>
    public async Task<IReadOnlyList<InstrumentSearchItem>> RecentAsync(CancellationToken cancellationToken)
    {
        var recent = await db.RecentSearches.AsNoTracking().OrderByDescending(r => r.SearchedAt).Take(RecentSearch.Keep).ToListAsync(cancellationToken);
        var summaries = (await instruments.GetAsync(recent.Select(r => r.InstrumentId).ToList(), cancellationToken)).ToDictionary(s => s.Id);
        return recent.Where(r => summaries.ContainsKey(r.InstrumentId)).Select(r => ToItem(summaries[r.InstrumentId])).ToList();
    }

    /// <summary>Records that the user opened <paramref name="symbol"/> from search; keeps the newest ten.</summary>
    public async Task<Result> AddRecentAsync(string symbol, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        if (await instruments.FindAsync(symbol, cancellationToken) is not { } instrument)
        {
            return InstrumentNotFound;
        }

        var now = timeProvider.GetUtcNow();
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO discovery.recent_searches (user_id, instrument_id, searched_at) VALUES ({userId}, {instrument.Id}, {now})
            ON CONFLICT (user_id, instrument_id) DO UPDATE SET searched_at = excluded.searched_at
            """,
            cancellationToken);
        await db.Database.ExecuteSqlAsync(
            $"""
            DELETE FROM discovery.recent_searches
            WHERE user_id = {userId} AND instrument_id NOT IN (
                SELECT instrument_id FROM discovery.recent_searches WHERE user_id = {userId} ORDER BY searched_at DESC LIMIT {RecentSearch.Keep})
            """,
            cancellationToken);
        return Result.Success();
    }

    private static InstrumentSearchItem ToItem(InstrumentSummary s) => new(s.Symbol, s.Name, s.Sector, s.Last, s.ChangePct, Held: false, SignalType: null);
}
