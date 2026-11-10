using System.Net;

namespace Astryx.Modules.Audit.Domain;

/// <summary>Read model of a row in <c>audit.audit_logs</c> (append-only; rows are never updated or deleted).</summary>
public sealed class AuditLogRecord
{
    public Guid Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public required string ActorType { get; set; }

    public Guid? UserId { get; set; }

    public required string Action { get; set; }

    public string? EntityType { get; set; }

    public string? EntityId { get; set; }

    public string? CorrelationId { get; set; }

    public IPAddress? Ip { get; set; }

    public string? UserAgent { get; set; }

    public string? Before { get; set; }

    public string? After { get; set; }

    public string? Metadata { get; set; }
}
