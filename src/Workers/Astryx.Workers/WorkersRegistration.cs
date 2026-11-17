using Astryx.Infrastructure.Jobs;
using Astryx.Infrastructure.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Workers;

/// <summary>
/// Registers BackgroundServices when Workers:Enabled is true (SYSTEM_DESIGN.md §19). Workers:Jobs=false keeps the outbox
/// dispatcher but not the job runner, for tests that run job handlers themselves.
/// </summary>
public static class WorkersRegistration
{
    public static IServiceCollection AddWorkers(this IServiceCollection services, IConfiguration configuration)
    {
        if (!configuration.GetValue("Workers:Enabled", true))
        {
            return services;
        }

        services.AddOutboxDispatcherService();
        return configuration.GetValue("Workers:Jobs", true) ? services.AddJobWorkerServices() : services;
    }
}
