using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Portfolio.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Modules.Portfolio;

/// <summary>Composition entry point for the Portfolio module (SYSTEM_DESIGN.md §7).</summary>
public static class PortfolioModule
{
    public static IServiceCollection AddPortfolioModule(this IServiceCollection services, IConfiguration configuration) =>
        services.AddModuleDbContext<PortfolioDbContext>(PortfolioDbContext.SchemaName, historyKey: "portfolio");
}
