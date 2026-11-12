using Astryx.Contracts.MarketData;
using Astryx.Modules.Watchlists.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Watchlists.Application;

/// <summary>Watched symbols across all users get fresh quotes and news first, after positions and orders (§16.3).</summary>
public sealed class WatchlistPrioritySymbols(WatchlistsDbContext db, IInstrumentDirectory instruments) : IPrioritySymbolSource
{
    public int Priority => 2;

    public async Task<IReadOnlyList<string>> GetSymbolsAsync(CancellationToken cancellationToken)
    {
        // Workers run without a user scope, so this sees every user's items; only symbols leave the module.
        var ids = await db.Items.AsNoTracking().Select(i => i.InstrumentId).Distinct().ToListAsync(cancellationToken);
        return ids.Count == 0 ? [] : (await instruments.GetAsync(ids, cancellationToken)).Select(s => s.Symbol).Order(StringComparer.Ordinal).ToList();
    }
}
