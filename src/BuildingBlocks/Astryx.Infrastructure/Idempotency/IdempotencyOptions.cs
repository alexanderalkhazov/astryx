namespace Astryx.Infrastructure.Idempotency;

/// <summary>Bound from configuration section <c>Idempotency</c>.</summary>
public sealed class IdempotencyOptions
{
    /// <summary>How long a completed response can be replayed (SYSTEM_DESIGN.md §20.10: 24 h).</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromHours(24);

    /// <summary>An in-progress claim older than this is treated as abandoned (process crashed mid-request).</summary>
    public TimeSpan InProgressLease { get; set; } = TimeSpan.FromSeconds(60);
}
