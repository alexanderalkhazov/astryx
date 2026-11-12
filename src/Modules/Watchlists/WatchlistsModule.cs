using Astryx.Contracts.Events.Identity;
using Astryx.Contracts.MarketData;
using Astryx.Infrastructure.Outbox;
using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Watchlists.Api;
using Astryx.Modules.Watchlists.Application;
using Astryx.Modules.Watchlists.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Astryx.Modules.Watchlists;

/// <summary>Composition entry point for the Watchlists module (SYSTEM_DESIGN.md §7.5).</summary>
public static class WatchlistsModule
{
    public static IServiceCollection AddWatchlistsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<WatchlistsDbContext>(WatchlistsDbContext.SchemaName, historyKey: "watchlists");
        services.AddIntegrationEventHandler<UserRegistered, CreateDefaultWatchlistHandler>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<WatchlistService>();
        services.AddScoped<IPrioritySymbolSource, WatchlistPrioritySymbols>();
        services.AddValidatorsFromAssemblyContaining<CreateWatchlistRequestValidator>(includeInternalTypes: true);
        return services;
    }
}
