using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Astryx.Infrastructure.Jobs;

public static class JobQueueRegistration
{
    /// <summary>Registers the queue, runner, scheduler and reaper (not their background loops; see <see cref="AddJobWorkerServices"/>).</summary>
    public static IServiceCollection AddJobQueue(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<JobQueueOptions>().Bind(configuration.GetSection("Jobs"));
        services.TryAddSingleton<JobRegistry>();
        services.TryAddSingleton<JobRunner>();
        services.TryAddSingleton<JobScheduler>();
        services.TryAddScoped<IJobQueue, JobQueue>();
        return services;
    }

    public static IServiceCollection AddJobWorkerServices(this IServiceCollection services) =>
        services.AddHostedService<JobRunnerService>().AddHostedService<JobMaintenanceService>();
}
