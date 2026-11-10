using Astryx.Infrastructure.Jobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Astryx.Infrastructure.Idempotency;

public static class IdempotencyRegistration
{
    public static IServiceCollection AddIdempotency(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<IdempotencyOptions>().Bind(configuration.GetSection("Idempotency"));
        services.TryAddScoped<IdempotencyFilter>();
        services.AddJobHandler<IdempotencyCleanupJob, IdempotencyCleanupHandler>();
        services.AddRecurringJob("idempotency-cleanup", TimeSpan.FromHours(1), _ => new IdempotencyCleanupJob());
        return services;
    }
}
