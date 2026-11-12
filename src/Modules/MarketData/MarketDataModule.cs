using Astryx.Infrastructure.Jobs;
using Astryx.Infrastructure.Persistence;
using Astryx.Modules.MarketData.Application.Ingestion;
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

        return services.AddIngestion(configuration);
    }

    /// <summary>Sync jobs (SYSTEM_DESIGN.md §16.3). Each runs on a short interval and decides itself whether it is due.</summary>
    private static IServiceCollection AddIngestion(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(IngestionOptions.SectionName);
        services.AddOptions<IngestionOptions>().Bind(section);
        services.AddSingleton<RateBudget>();
        services.AddSingleton<QuoteRotation>();
        services.AddScoped<MarketClock>();
        services.AddScoped<SyncStateStore>();
        services.AddScoped<SyncRunner>();
        services.AddScoped<NewsIngestor>();

        services.AddJobHandler<InstrumentSyncJob, InstrumentSyncHandler>();
        services.AddJobHandler<CalendarSyncJob, CalendarSyncHandler>();
        services.AddJobHandler<EodBarSyncJob, EodBarSyncHandler>();
        services.AddJobHandler<QuoteSyncJob, QuoteSyncHandler>();
        services.AddJobHandler<FundamentalsSyncJob, FundamentalsSyncHandler>();
        services.AddJobHandler<NewsSyncJob, NewsSyncHandler>();

        if (section.GetValue(nameof(IngestionOptions.Enabled), true))
        {
            services.AddRecurringJob("market.quote_sync", TimeSpan.FromMinutes(1), _ => new QuoteSyncJob());
            services.AddRecurringJob("market.eod_bar_sync", TimeSpan.FromMinutes(5), _ => new EodBarSyncJob());
            services.AddRecurringJob("market.instrument_sync", TimeSpan.FromMinutes(15), _ => new InstrumentSyncJob());
            services.AddRecurringJob("market.fundamentals_sync", TimeSpan.FromMinutes(15), _ => new FundamentalsSyncJob());
            services.AddRecurringJob("market.news_sync", TimeSpan.FromMinutes(15), _ => new NewsSyncJob());
            services.AddRecurringJob("market.calendar_sync", TimeSpan.FromHours(1), _ => new CalendarSyncJob());
        }

        return services;
    }
}
