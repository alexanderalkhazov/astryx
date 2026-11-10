using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Paper.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Modules.Paper;

/// <summary>Composition entry point for the Paper module (SYSTEM_DESIGN.md §7).</summary>
public static class PaperModule
{
    public static IServiceCollection AddPaperModule(this IServiceCollection services, IConfiguration configuration) =>
        services.AddModuleDbContext<PaperDbContext>(PaperDbContext.SchemaName, historyKey: "paper");
}
