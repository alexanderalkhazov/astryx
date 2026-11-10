using System.Text.Json;
using Astryx.Contracts.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Infrastructure.Outbox;

/// <summary>A handler bound to an event name, able to deserialise the payload and invoke itself in a DI scope.</summary>
public sealed record IntegrationEventHandlerDescriptor(
    string EventName,
    string HandlerName,
    Func<IServiceProvider, string, IntegrationEventContext, CancellationToken, Task> InvokeAsync);

/// <summary>All registered handlers, keyed by stable event name.</summary>
public sealed class IntegrationEventRegistry(IEnumerable<IntegrationEventHandlerDescriptor> descriptors)
{
    private readonly ILookup<string, IntegrationEventHandlerDescriptor> _byEvent = descriptors.ToLookup(d => d.EventName, StringComparer.Ordinal);

    public IEnumerable<IntegrationEventHandlerDescriptor> HandlersFor(string eventName) => _byEvent[eventName];
}

public static class IntegrationEventRegistration
{
    public static IServiceCollection AddIntegrationEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IIntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        services.AddScoped<THandler>();
        services.AddSingleton(new IntegrationEventHandlerDescriptor(
            TEvent.EventName,
            typeof(THandler).FullName!,
            async (provider, payload, context, cancellationToken) =>
            {
                var integrationEvent = JsonSerializer.Deserialize<TEvent>(payload, OutboxSerializer.Options)
                    ?? throw new JsonException($"Payload for {TEvent.EventName} deserialised to null.");
                await provider.GetRequiredService<THandler>().HandleAsync(integrationEvent, context, cancellationToken);
            }));
        return services;
    }
}
