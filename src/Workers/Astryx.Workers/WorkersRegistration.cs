using Astryx.Infrastructure.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Workers;

/// <summary>Registers BackgroundServices when Workers:Enabled is true (SYSTEM_DESIGN.md §19).</summary>
public static class WorkersRegistration
{
    public static IServiceCollection AddWorkers(this IServiceCollection services, IConfiguration configuration)
    {
        if (!configuration.GetValue("Workers:Enabled", true))
        {
            return services;
        }

        return services.AddOutboxDispatcherService();
    }
}
