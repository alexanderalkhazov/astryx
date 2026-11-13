using Astryx.Contracts.Email;
using Astryx.Contracts.Events.Ai;
using Astryx.Contracts.Events.Paper;
using Astryx.Infrastructure.Jobs;
using Astryx.Infrastructure.Outbox;
using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Notifications.Application;
using Astryx.Modules.Notifications.Infrastructure.Email;
using Astryx.Modules.Notifications.Infrastructure.Persistence;
using Astryx.Modules.Notifications.Infrastructure.Push;
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
        services.AddScoped<PushDevices>();
        services.AddJobHandler<PushNotificationJob, PushSender>();
        // Push:Provider: "expo" sends through exp.host; anything else records pushes (dev and tests). Features:Push gates it all.
        if (string.Equals(configuration["Push:Provider"], "expo", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<IPushGateway, ExpoPushGateway>(http =>
            {
                http.BaseAddress = new Uri(configuration["Push:ExpoBaseUrl"] ?? "https://exp.host/");
                http.Timeout = TimeSpan.FromSeconds(10);
                if (configuration["Push:ExpoAccessToken"] is { Length: > 0 } token)
                {
                    http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                }
            });
        }
        else
        {
            services.AddSingleton<FakePushGateway>();
            services.AddSingleton<IPushGateway>(sp => sp.GetRequiredService<FakePushGateway>());
        }

        services.AddIntegrationEventHandler<OrderFilled, NotificationWriter>();
        services.AddIntegrationEventHandler<PositionClosed, NotificationWriter>();
        services.AddIntegrationEventHandler<OrderExpired, NotificationWriter>();
        services.AddIntegrationEventHandler<StopProximityReached, NotificationWriter>();
        services.AddIntegrationEventHandler<AnalysisUpdated, NotificationWriter>();
        services.AddIntegrationEventHandler<Astryx.Contracts.Events.Strategies.StrategyGuardChanged, NotificationWriter>();
        services.AddIntegrationEventHandler<Astryx.Contracts.Events.Paper.PaperAccountReset, NotificationWriter>();
        return services;
    }
}
