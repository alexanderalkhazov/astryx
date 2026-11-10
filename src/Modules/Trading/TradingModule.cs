using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Modules.Trading;

/// <summary>Composition entry point for the Trading module (SYSTEM_DESIGN.md §7). NOT IMPLEMENTED.</summary>
public static class TradingModule
{
    public static IServiceCollection AddTradingModule(this IServiceCollection services, IConfiguration configuration) => services;
}
