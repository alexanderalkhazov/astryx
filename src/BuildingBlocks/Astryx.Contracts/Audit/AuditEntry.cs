namespace Astryx.Contracts.Audit;

/// <summary>
/// One security- or money-relevant action for the append-only audit trail (SYSTEM_DESIGN.md §7.18).
/// <see cref="Before"/>, <see cref="After"/> and <see cref="Metadata"/> are serialised to JSON and redacted
/// (passwords, tokens, keys, credentials removed; emails masked) before storage.
/// </summary>
public sealed record AuditEntry(
    string Action,
    string ActorType,
    Guid? UserId = null,
    string? EntityType = null,
    string? EntityId = null,
    object? Before = null,
    object? After = null,
    object? Metadata = null);

public static class AuditActor
{
    public const string User = "user";
    public const string System = "system";
    public const string Worker = "worker";
    public const string Admin = "admin";
}

/// <summary>Writes an audit entry on its own, outside any business transaction (e.g. a failed login).</summary>
public interface IAuditLog
{
    Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}
