using Astryx.Modules.Watchlists.Domain;
using Astryx.Modules.Watchlists.Infrastructure.Persistence;
using Astryx.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Watchlists.Application;

/// <summary>Creates a user's default watchlist. Idempotent and race-safe: the partial unique index allows one per user.</summary>
internal static class DefaultWatchlist
{
    public static async Task EnsureAsync(WatchlistsDbContext db, Guid userId, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO watchlist.watchlists (id, user_id, name, is_default, sort_order, version, created_at, updated_at)
            VALUES ({Ids.New(timeProvider)}, {userId}, {Watchlist.DefaultName}, TRUE, 0, 1, {now}, {now})
            ON CONFLICT DO NOTHING
            """,
            cancellationToken);
    }
}
