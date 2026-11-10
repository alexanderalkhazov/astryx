using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Modules.Paper;

/// <summary>Composition entry point for the Paper module (SYSTEM_DESIGN.md §7). NOT IMPLEMENTED.</summary>
public static class PaperModule
{
    public static IServiceCollection AddPaperModule(this IServiceCollection services, IConfiguration configuration) => services;
}
