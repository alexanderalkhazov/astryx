using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Notifications.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Modules.Notifications;

/// <summary>Composition entry point for the Notifications module (SYSTEM_DESIGN.md §7).</summary>
public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, IConfiguration configuration) =>
        services.AddModuleDbContext<NotificationsDbContext>(NotificationsDbContext.SchemaName, historyKey: "notifications");
}
