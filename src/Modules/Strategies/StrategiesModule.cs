using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Modules.Strategies;

/// <summary>Composition entry point for the Strategies module (SYSTEM_DESIGN.md §7). NOT IMPLEMENTED.</summary>
public static class StrategiesModule
{
    public static IServiceCollection AddStrategiesModule(this IServiceCollection services, IConfiguration configuration) => services;
}
