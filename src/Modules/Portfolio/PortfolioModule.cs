using Astryx.Contracts.Events.Identity;
using Astryx.Infrastructure.Jobs;
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
        services.AddScoped<Astryx.Contracts.Portfolio.IPortfolioLedger, PortfolioLedger>();
        services.AddScoped<Astryx.Contracts.Portfolio.IPortfolioDirectory, PortfolioDirectory>();
        services.AddScoped<Astryx.Contracts.Portfolio.IBrokerPortfolios, BrokerPortfolios>();
        services.AddScoped<Astryx.Contracts.Portfolio.IPortfolioStateSource, PortfolioStateSource>();
        services.AddScoped<Astryx.Contracts.Portfolio.IPositionBook, PositionBook>();
        services.AddScoped<Astryx.Contracts.Portfolio.IPortfolioEpochs, PortfolioEpochs>();
        services.AddScoped<ValuationService>();
        services.AddScoped<ExposureService>();
        services.AddScoped<PositionDetailService>();
        services.AddJobHandler<PortfolioSnapshotJob, PortfolioSnapshotHandler>();
        services.AddRecurringJob("portfolio-snapshot", TimeSpan.FromMinutes(15), _ => new PortfolioSnapshotJob());
        services.AddJobHandler<StartOfDayJob, StartOfDayHandler>();
        services.AddScoped<PortfolioReconciler>();
        services.AddJobHandler<ReconciliationJob, ReconciliationHandler>();
        services.AddRecurringJob("portfolio-reconciliation", TimeSpan.FromMinutes(30), _ => new ReconciliationJob());
        services.AddRecurringJob("portfolio-start-of-day", TimeSpan.FromMinutes(5), _ => new StartOfDayJob());
        return services;
    }
}
