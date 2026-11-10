using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Modules.Ai;

/// <summary>Composition entry point for the Ai module (SYSTEM_DESIGN.md §7). NOT IMPLEMENTED.</summary>
public static class AiModule
{
    public static IServiceCollection AddAiModule(this IServiceCollection services, IConfiguration configuration) => services;
}
