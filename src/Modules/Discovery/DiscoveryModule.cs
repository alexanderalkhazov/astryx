using Astryx.Infrastructure.Persistence;
using Astryx.Infrastructure.Jobs;
using Astryx.Modules.Discovery.Application;
using Astryx.Modules.Discovery.Application.Scanner;
using Astryx.Modules.Discovery.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Astryx.Modules.Discovery;

/// <summary>Composition entry point for the Discovery module (SYSTEM_DESIGN.md §7).</summary>
public static class DiscoveryModule
{
    public static IServiceCollection AddDiscoveryModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<DiscoveryDbContext>(DiscoveryDbContext.SchemaName, historyKey: "discovery");
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<SearchService>();

        services.AddOptions<ScannerOptions>().Bind(configuration.GetSection(ScannerOptions.SectionName));
        services.AddScoped<ScannerService>();
        services.AddScoped<Astryx.Modules.Discovery.Application.Signals.SignalQueries>();
        services.AddScoped<Astryx.Contracts.MarketData.IChartLevelsSource, SignalChartLevels>();
        services.AddScoped<Astryx.Contracts.Discovery.IActiveSignalSource, ActiveSignalSource>();
        services.AddScoped<Astryx.Contracts.Discovery.ISignalLookup, SignalLookup>();
        services.AddScoped<Astryx.Contracts.Discovery.IStrategySignalFeed, StrategySignalFeed>();
        services.AddScoped<Astryx.Contracts.MarketData.IPrioritySymbolSource, SignalPrioritySymbols>();
        services.AddJobHandler<ScannerRunJob, ScannerRunHandler>();
        var scanner = configuration.GetSection(ScannerOptions.SectionName).Get<ScannerOptions>() ?? new ScannerOptions();
        if (scanner.Enabled)
        {
            services.AddRecurringJob("discovery.scanner_run", TimeSpan.FromMinutes(scanner.IntervalMinutes), _ => new ScannerRunJob());
        }

        return services;
    }
}
