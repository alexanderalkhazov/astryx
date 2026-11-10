using Astryx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Watchlists.Infrastructure.Persistence;

/// <summary>Watchlists module data in schema <c>watchlist</c> (SYSTEM_DESIGN.md §10.4). No tables yet.</summary>
public sealed class WatchlistsDbContext(DbContextOptions<WatchlistsDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "watchlist";

    public override string Schema => SchemaName;
}
