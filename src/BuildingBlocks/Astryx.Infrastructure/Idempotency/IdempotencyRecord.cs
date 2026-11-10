namespace Astryx.Infrastructure.Idempotency;

/// <summary>Row in <c>infra.idempotency_records</c> (SYSTEM_DESIGN.md §10.4, §20.10).</summary>
public sealed class IdempotencyRecord
{
    /// <summary>Who the key belongs to: the user id, or <c>anonymous</c> on unauthenticated endpoints.</summary>
    public required string Subject { get; set; }

    public required string Key { get; set; }

    public required string Endpoint { get; set; }

    public required string RequestHash { get; set; }

    public required string Status { get; set; }

    /// <summary>While <c>in_progress</c>: after this instant the request counts as abandoned and may be taken over.</summary>
    public DateTimeOffset InProgressUntil { get; set; }

    public int? ResponseStatus { get; set; }

    public string? ResponseContentType { get; set; }

    public string? ResponseBody { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}

public static class IdempotencyStatus
{
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
}
