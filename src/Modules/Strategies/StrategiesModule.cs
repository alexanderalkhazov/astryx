using Astryx.Infrastructure.Jobs;
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
        services.AddSingleton<IStrategyRule, TrendMomentumRule>(); // T-163: the four rules approved 2026-11-23
        services.AddSingleton<IStrategyRule, TrendPullbackRule>();
        services.AddSingleton<IStrategyRule, Rsi2MeanReversionRule>();
        services.AddSingleton<IStrategyRule, EarningsDriftRule>();
        services.AddJobHandler<DrawdownGuardJob, DrawdownGuardHandler>();
        services.AddScoped<Application.Backtesting.BacktestService>();
        services.AddScoped<Astryx.Contracts.Strategies.IStrategyEvidence, StrategyEvidenceSource>(); // T-166
        services.AddJobHandler<Application.Backtesting.BacktestJob, Application.Backtesting.BacktestService>();
        services.AddRecurringJob("strategies-drawdown-guard", TimeSpan.FromHours(1), _ => new DrawdownGuardJob());
        services.AddJobHandler<StrategyExitJob, StrategyExitHandler>();
        services.AddRecurringJob("strategies-exit", TimeSpan.FromMinutes(5), _ => new StrategyExitJob()); // T-163
        services.AddJobHandler<TrackRecordJob, TrackRecordHandler>();
        services.AddRecurringJob("strategies-track-record", TimeSpan.FromHours(4), _ => new TrackRecordJob()); // T-166
        return services;
    }
}
