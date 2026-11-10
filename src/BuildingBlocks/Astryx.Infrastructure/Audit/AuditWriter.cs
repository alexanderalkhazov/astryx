using Astryx.Contracts.Audit;
using Astryx.Infrastructure.Outbox;
using Astryx.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Astryx.Infrastructure.Audit;

/// <summary>Client details of the current request, captured by the correlation middleware for audit rows.</summary>
public static class ClientContext
{
    private static readonly AsyncLocal<(string? Ip, string? UserAgent)> Current = new();

    public static string? Ip => Current.Value.Ip;

    public static string? UserAgent => Current.Value.UserAgent;

    public static void Set(string? ip, string? userAgent) =>
        Current.Value = (ip, userAgent is { Length: > 256 } ? userAgent[..256] : userAgent);
}

/// <summary>Inserts one audit row on the given connection, joining whatever transaction is open on it.</summary>
public static class AuditWriter
{
    public static Task InsertAsync(DatabaseFacade database, AuditEntry entry, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        var id = Ids.New();
        var before = AuditRedactor.ToRedactedJson(entry.Before);
        var after = AuditRedactor.ToRedactedJson(entry.After);
        var metadata = AuditRedactor.ToRedactedJson(entry.Metadata);
        return database.ExecuteSqlAsync(
            $"""
            INSERT INTO audit.audit_logs (id, occurred_at, actor_type, user_id, action, entity_type, entity_id, correlation_id, ip, user_agent, before, after, metadata)
            VALUES ({id}, {occurredAt}, {entry.ActorType}, {entry.UserId}, {entry.Action}, {entry.EntityType}, {entry.EntityId}, {CorrelationContext.Id},
                    CAST({ClientContext.Ip} AS inet), {ClientContext.UserAgent}, CAST({before} AS jsonb), CAST({after} AS jsonb), CAST({metadata} AS jsonb))
            """,
            cancellationToken);
    }
}
