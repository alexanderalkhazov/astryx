using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Strategies.Application;
using Astryx.Modules.Strategies.Application.Rules;
using Astryx.Modules.Strategies.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Modules.Strategies;

/// <summary>Composition entry point for the Strategies module (SYSTEM_DESIGN.md §7).</summary>
public static class StrategiesModule
{
    public static IServiceCollection AddStrategiesModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<StrategiesDbContext>(StrategiesDbContext.SchemaName, historyKey: "strategies");
        services.AddScoped<StrategyCatalog>();
        services.AddScoped<StrategyEvaluator>();
        services.AddScoped<Astryx.Contracts.Strategies.IStrategySignals, StrategySignals>();
        services.AddSingleton<IStrategyRule, BaseBreakoutRule>();
        return services;
    }
}
