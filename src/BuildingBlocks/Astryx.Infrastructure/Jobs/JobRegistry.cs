using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Infrastructure.Jobs;

public sealed record JobHandlerDescriptor(string JobType, Func<IServiceProvider, string, JobContext, CancellationToken, Task> InvokeAsync);

/// <summary>A recurring schedule: every <see cref="Interval"/>, enqueue the job produced by <see cref="CreatePayload"/>.</summary>
public sealed record RecurringJobDefinition(string Name, string JobType, TimeSpan Interval, Func<DateTimeOffset, string> CreatePayload);

public sealed class JobRegistry(IEnumerable<JobHandlerDescriptor> handlers, IEnumerable<RecurringJobDefinition> recurring)
{
    private readonly Dictionary<string, JobHandlerDescriptor> _handlers = handlers.ToDictionary(h => h.JobType, StringComparer.Ordinal);

    public IReadOnlyList<RecurringJobDefinition> Recurring { get; } = recurring.ToList();

    public JobHandlerDescriptor? HandlerFor(string jobType) => _handlers.GetValueOrDefault(jobType);
}

public static class JobRegistration
{
    public static IServiceCollection AddJobHandler<TJob, THandler>(this IServiceCollection services)
        where TJob : IJob
        where THandler : class, IJobHandler<TJob>
    {
        services.AddScoped<THandler>();
        services.AddSingleton(new JobHandlerDescriptor(
            TJob.JobType,
            async (provider, payload, context, cancellationToken) =>
            {
                var job = JsonSerializer.Deserialize<TJob>(payload, InfrastructureJson.Options)
                    ?? throw new JsonException($"Payload for {TJob.JobType} deserialised to null.");
                await provider.GetRequiredService<THandler>().HandleAsync(job, context, cancellationToken);
            }));
        return services;
    }

    /// <summary>Enqueues <typeparamref name="TJob"/> once per <paramref name="interval"/> across all replicas.</summary>
    public static IServiceCollection AddRecurringJob<TJob>(this IServiceCollection services, string name, TimeSpan interval, Func<DateTimeOffset, TJob> create)
        where TJob : IJob =>
        services.AddSingleton(new RecurringJobDefinition(name, TJob.JobType, interval, slot => JsonSerializer.Serialize(create(slot), InfrastructureJson.Options)));
}
