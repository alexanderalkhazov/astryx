namespace Astryx.Contracts.Events.Ai;

/// <summary>
/// An analysis job changed status (SYSTEM_DESIGN.md §9.6 <c>analysis.updated</c>): processing, back to pending for a
/// retry, or finished. Delivered to the user's devices over SignalR; clients that miss it poll AI2.
/// </summary>
public sealed record AnalysisUpdated(Guid UserId, Guid JobId, string Symbol, string Status, int Attempt, Guid? ResultId, string? ErrorCode) : IIntegrationEvent
{
    public static string EventName => "ai.analysis_updated.v1";
}
