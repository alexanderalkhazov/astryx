using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Modules.Quant;

/// <summary>Composition entry point for the Quant module (SYSTEM_DESIGN.md §7). NOT IMPLEMENTED.</summary>
public static class QuantModule
{
    public static IServiceCollection AddQuantModule(this IServiceCollection services, IConfiguration configuration) => services;
}
