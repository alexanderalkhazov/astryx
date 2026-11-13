using Astryx.Infrastructure.Jobs;
using Astryx.Infrastructure.Outbox;
using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Paper.Application;
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
        services.AddScoped<PaperOrderService>();
        services.AddScoped<PaperExecution>();
        services.AddScoped<PositionActions>();
        services.AddIntegrationEventHandler<Astryx.Contracts.Events.MarketData.QuotesUpdated, PositionMonitor>();
        services.AddJobHandler<DayOrderExpiryJob, DayOrderExpiryHandler>();
        services.AddRecurringJob("paper-day-order-expiry", TimeSpan.FromMinutes(1), _ => new DayOrderExpiryJob());
        services.AddScoped<Astryx.Contracts.Paper.IOpenOrders, OpenOrders>();
        return services;
    }
}
