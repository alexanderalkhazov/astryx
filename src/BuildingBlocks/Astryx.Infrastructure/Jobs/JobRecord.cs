namespace Astryx.Infrastructure.Jobs;

public static class JobStatus
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Dead = "dead";
}

/// <summary>Row in <c>infra.job_queue</c> (SYSTEM_DESIGN.md §10.4).</summary>
public sealed class JobRecord
{
    public Guid Id { get; set; }

    public required string Type { get; set; }

    public required string Payload { get; set; }

    public required string Status { get; set; }

    public DateTimeOffset RunAfter { get; set; }

    public int Attempts { get; set; }

    public int MaxAttempts { get; set; }

    public int TimeoutSeconds { get; set; }

    public string? LockedBy { get; set; }

    public DateTimeOffset? LockedUntil { get; set; }

    public string? DedupeKey { get; set; }

    public string? CorrelationId { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }
}

/// <summary>Row in <c>infra.recurring_jobs</c>: the next due slot of one recurring schedule.</summary>
public sealed class RecurringJobState
{
    public required string Name { get; set; }

    public DateTimeOffset NextDueAt { get; set; }
}
