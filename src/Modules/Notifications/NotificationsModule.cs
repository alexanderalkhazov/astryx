using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Modules.Notifications;

/// <summary>Composition entry point for the Notifications module (SYSTEM_DESIGN.md §7). NOT IMPLEMENTED.</summary>
public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, IConfiguration configuration) => services;
}
