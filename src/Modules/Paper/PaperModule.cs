using Astryx.Infrastructure.Alpaca;
using Astryx.Infrastructure.Jobs;
using Astryx.Infrastructure.Outbox;
using Astryx.Infrastructure.Persistence;
using Astryx.Infrastructure.Security;
using Astryx.Modules.Paper.Application;
using Astryx.Modules.Paper.Infrastructure.Alpaca;
using Astryx.Modules.Paper.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Modules.Paper;

/// <summary>Composition entry point for the Paper module (SYSTEM_DESIGN.md §7.11, §15).</summary>
public static class PaperModule
{
    public static IServiceCollection AddPaperModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<PaperDbContext>(PaperDbContext.SchemaName, historyKey: "paper");

        // Alpaca Paper execution (§17, T-140–T-145): the trading client, sealed credentials and connections.
        services.AddAlpacaCore(configuration);
        services.Configure<CredentialVaultOptions>(configuration.GetSection(CredentialVaultOptions.SectionName));
        services.AddSingleton<CredentialVault>();
        services.AddSingleton<IAlpacaTradingClient, AlpacaTradingClient>();
        services.AddScoped<BrokerCredentials>();
        services.AddMemoryCache();
        services.AddScoped<Astryx.Contracts.Paper.IBrokerReadiness, BrokerReadinessService>();
        services.AddSingleton<Astryx.Contracts.Paper.IPositionCloser, PositionCloser>();
        services.AddScoped<BrokerConnectionService>();
        services.AddSingleton<AlpacaExecution>();
        services.AddSingleton<IAlpacaTradeUpdates, AlpacaTradeUpdatesStream>();
        services.AddSingleton<TradeUpdatesListener>();
        if (configuration.GetValue("Workers:Enabled", true) && configuration.GetValue("Workers:Jobs", true) && configuration.GetValue("Alpaca:Streaming", true))
        {
            services.AddHostedService(sp => sp.GetRequiredService<TradeUpdatesListener>()); // T-148: the fast path; BrokerSyncJob stays the fallback
        }
        services.AddSingleton<Astryx.Contracts.Paper.IBrokerSync>(sp => sp.GetRequiredService<AlpacaExecution>());
        services.AddJobHandler<SubmitBrokerOrderJob, SubmitBrokerOrderHandler>();
        services.AddJobHandler<SettleBrokerOrderJob, SettleBrokerOrderHandler>();
        services.AddJobHandler<BrokerSyncJob, BrokerSyncHandler>();
        services.AddRecurringJob("paper.broker_sync", TimeSpan.FromMinutes(1), _ => new BrokerSyncJob());
        services.AddScoped<PaperOrderService>();
        services.AddScoped<PaperExecution>();
        services.AddScoped<PositionActions>();
        services.AddScoped<PaperResetService>();
        services.AddIntegrationEventHandler<Astryx.Contracts.Events.MarketData.QuotesUpdated, PositionMonitor>();
        services.AddJobHandler<DayOrderExpiryJob, DayOrderExpiryHandler>();
        services.AddIntegrationEventHandler<Astryx.Contracts.Events.Paper.OrderAccepted, PaperRealtimeRelays>();
        services.AddIntegrationEventHandler<Astryx.Contracts.Events.Paper.OrderFilled, PaperRealtimeRelays>();
        services.AddIntegrationEventHandler<Astryx.Contracts.Events.Paper.OrderExpired, PaperRealtimeRelays>();
        services.AddIntegrationEventHandler<Astryx.Contracts.Events.Paper.OrderRejected, PaperRealtimeRelays>();
        services.AddIntegrationEventHandler<Astryx.Contracts.Events.Paper.PositionOpened, PaperRealtimeRelays>();
        services.AddIntegrationEventHandler<Astryx.Contracts.Events.Paper.PositionClosed, PaperRealtimeRelays>();
        services.AddRecurringJob("paper-day-order-expiry", TimeSpan.FromMinutes(1), _ => new DayOrderExpiryJob());
        services.AddScoped<Astryx.Contracts.Paper.IOpenOrders, OpenOrders>();
        services.AddScoped<Astryx.Contracts.Paper.IOrderLookup, OrderLookup>();
        services.AddScoped<Astryx.Contracts.Paper.IPaperReconciliationFacts, PaperReconciliationFacts>();
        return services;
    }
}
