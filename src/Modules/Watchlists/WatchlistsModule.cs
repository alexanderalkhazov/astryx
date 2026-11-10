using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Modules.Watchlists;

/// <summary>Composition entry point for the Watchlists module (SYSTEM_DESIGN.md §7). NOT IMPLEMENTED.</summary>
public static class WatchlistsModule
{
    public static IServiceCollection AddWatchlistsModule(this IServiceCollection services, IConfiguration configuration) => services;
}
