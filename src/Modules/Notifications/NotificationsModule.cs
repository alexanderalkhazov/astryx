using Astryx.Contracts.Email;
using Astryx.Contracts.Events.Ai;
using Astryx.Contracts.Events.Paper;
using Astryx.Infrastructure.Outbox;
using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Notifications.Application;
using Astryx.Modules.Notifications.Infrastructure.Email;
using Astryx.Modules.Notifications.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Modules.Notifications;

/// <summary>Composition entry point for the Notifications module (SYSTEM_DESIGN.md §7.15).</summary>
public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SmtpOptions>().Bind(configuration.GetSection("Email:Smtp"));
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddModuleDbContext<NotificationsDbContext>(NotificationsDbContext.SchemaName, historyKey: "notifications");
        services.AddScoped<NotificationFeed>();
        services.AddIntegrationEventHandler<OrderFilled, NotificationWriter>();
        services.AddIntegrationEventHandler<PositionClosed, NotificationWriter>();
        services.AddIntegrationEventHandler<OrderExpired, NotificationWriter>();
        services.AddIntegrationEventHandler<StopProximityReached, NotificationWriter>();
        services.AddIntegrationEventHandler<AnalysisUpdated, NotificationWriter>();
        services.AddIntegrationEventHandler<Astryx.Contracts.Events.Strategies.StrategyGuardChanged, NotificationWriter>();
        return services;
    }
}
