using Astryx.Contracts.Events.Identity;
using Astryx.Infrastructure.Outbox;
using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Portfolio.Application;
using Astryx.Modules.Portfolio.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Modules.Portfolio;

/// <summary>Composition entry point for the Portfolio module (SYSTEM_DESIGN.md §7.12).</summary>
public static class PortfolioModule
{
    public static IServiceCollection AddPortfolioModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<PortfolioDbContext>(PortfolioDbContext.SchemaName, historyKey: "portfolio");
        services.AddIntegrationEventHandler<UserRegistered, CreatePaperPortfolioHandler>();
        return services;
    }
}
