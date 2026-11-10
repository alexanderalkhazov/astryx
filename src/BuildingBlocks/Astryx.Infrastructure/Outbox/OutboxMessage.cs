namespace Astryx.Infrastructure.Outbox;

/// <summary>Row in <c>infra.outbox_messages</c> (SYSTEM_DESIGN.md §10.4).</summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public required string Type { get; set; }

    public required string Payload { get; set; }

    public string? CorrelationId { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public string? LastError { get; set; }

    /// <summary>Set when the message is poison (max attempts reached or undeserialisable); it is never retried.</summary>
    public DateTimeOffset? FailedAt { get; set; }
}

/// <summary>Row in <c>infra.outbox_deliveries</c>: one handler has successfully processed one message.</summary>
public sealed class OutboxDelivery
{
    public Guid MessageId { get; set; }

    public required string Handler { get; set; }

    public DateTimeOffset DeliveredAt { get; set; }
}
