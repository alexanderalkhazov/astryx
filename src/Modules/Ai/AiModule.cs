using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Ai.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Modules.Ai;

/// <summary>Composition entry point for the Ai module (SYSTEM_DESIGN.md §7).</summary>
public static class AiModule
{
    public static IServiceCollection AddAiModule(this IServiceCollection services, IConfiguration configuration) =>
        services.AddModuleDbContext<AiDbContext>(AiDbContext.SchemaName, historyKey: "ai");
}
