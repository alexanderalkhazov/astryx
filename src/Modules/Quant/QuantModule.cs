using Astryx.Infrastructure.Outbox;
using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Quant.Application.Analysis;
using Astryx.Modules.Quant.Application.Indicators;
using Astryx.Modules.Quant.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Astryx.Modules.Quant;

/// <summary>Composition entry point for the Quant module (SYSTEM_DESIGN.md §7).</summary>
public static class QuantModule
{
    public static IServiceCollection AddQuantModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<QuantDbContext>(QuantDbContext.SchemaName, historyKey: "quant");
        services.AddSingleton<IIndicatorCalculator, IndicatorCalculator>();
        services.AddSingleton<StockAnalysisEngine>();
        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<RelativeStrengthRanks>();
        services.AddScoped<IndicatorSnapshotService>();
        services.AddIntegrationEventHandler<Astryx.Contracts.Events.MarketData.CorporateActionRecorded, IndicatorCacheInvalidation>();
        services.AddScoped<Astryx.Contracts.Quant.IIndicatorSnapshots>(sp => sp.GetRequiredService<IndicatorSnapshotService>());
        var realData = !string.Equals(configuration["MarketData:Provider"] ?? "Fake", "Fake", StringComparison.OrdinalIgnoreCase);
        services.AddSingleton(new FormingVolumePolicy(configuration.GetValue("Quant:ProjectFormingVolume", realData)));
        services.AddScoped<Astryx.Contracts.Quant.IQuantFacts, QuantFactsService>();
        services.AddScoped<Astryx.Contracts.Quant.IBacktestData, BacktestDataService>();
        return services;
    }
}
