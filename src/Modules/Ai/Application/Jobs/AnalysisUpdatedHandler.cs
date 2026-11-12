using Astryx.Contracts.Events;
using Astryx.Contracts.Events.Ai;
using Astryx.Infrastructure.Realtime;

namespace Astryx.Modules.Ai.Application.Jobs;

/// <summary>Relays <see cref="AnalysisUpdated"/> to the user's devices as <c>analysis.updated</c> (§9.6). A re-delivery just repeats the push.</summary>
public sealed class AnalysisUpdatedHandler(IRealtimeNotifier notifier) : IIntegrationEventHandler<AnalysisUpdated>
{
    public const string EventName = "analysis.updated";

    public Task HandleAsync(AnalysisUpdated integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken) =>
        notifier.SendToUserAsync(
            integrationEvent.UserId,
            EventName,
            new AnalysisUpdatedMessage(integrationEvent.JobId, integrationEvent.Symbol, integrationEvent.Status, integrationEvent.Attempt, integrationEvent.ResultId, integrationEvent.ErrorCode),
            cancellationToken);
}

/// <summary>The <c>analysis.updated</c> payload.</summary>
public sealed record AnalysisUpdatedMessage(Guid JobId, string Symbol, string Status, int Attempt, Guid? ResultId, string? ErrorCode);
