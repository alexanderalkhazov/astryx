using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Modules.MarketData;

/// <summary>Composition entry point for the MarketData module (SYSTEM_DESIGN.md §7). NOT IMPLEMENTED.</summary>
public static class MarketDataModule
{
    public static IServiceCollection AddMarketDataModule(this IServiceCollection services, IConfiguration configuration) => services;
}
