using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Astryx.Infrastructure.Outbox;

public static class OutboxRegistration
{
    /// <summary>Registers the dispatcher (not its background loop; see <see cref="AddOutboxDispatcherService"/>).</summary>
    public static IServiceCollection AddOutbox(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<OutboxOptions>().Bind(configuration.GetSection("Outbox"));
        services.TryAddSingleton<IntegrationEventRegistry>();
        services.TryAddSingleton<OutboxDispatcher>();
        return services;
    }

    public static IServiceCollection AddOutboxDispatcherService(this IServiceCollection services) =>
        services.AddHostedService<OutboxDispatcherService>();
}
