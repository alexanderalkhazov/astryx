using Astryx.Infrastructure.Persistence;
using Astryx.Modules.MarketData.Application.Providers;
using Astryx.Modules.MarketData.Infrastructure.Persistence;
using Astryx.Modules.MarketData.Infrastructure.Providers.Fake;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Astryx.Modules.MarketData;

/// <summary>Composition entry point for the MarketData module (SYSTEM_DESIGN.md §7).</summary>
public static class MarketDataModule
{
    public static IServiceCollection AddMarketDataModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<MarketDataDbContext>(MarketDataDbContext.SchemaName, historyKey: "marketdata");

        // Only the fake provider exists until the provider decision (T-051, Q-03) picks a real one.
        var provider = configuration["MarketData:Provider"] ?? FakeMarketDataProvider.ProviderCode;
        if (!string.Equals(provider, FakeMarketDataProvider.ProviderCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"MarketData:Provider '{provider}' is not implemented; only '{FakeMarketDataProvider.ProviderCode}' is available (SYSTEM_DESIGN.md §16.2, Q-03).");
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<FakeMarketDataOptions>().Bind(configuration.GetSection(FakeMarketDataOptions.SectionName));
        services.AddSingleton(_ => FixtureData.Load());
        services.AddSingleton<FakeCalendar>();
        services.AddSingleton<MarketDataFaults>();
        services.AddSingleton<IMarketDataProvider, FakeMarketDataProvider>();
        services.AddSingleton<INewsProvider, FakeNewsProvider>();
        return services;
    }
}
