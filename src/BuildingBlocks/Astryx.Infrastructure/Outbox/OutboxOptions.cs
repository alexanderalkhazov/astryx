namespace Astryx.Infrastructure.Outbox;

/// <summary>Bound from configuration section <c>Outbox</c>.</summary>
public sealed class OutboxOptions
{
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    public int BatchSize { get; set; } = 50;

    /// <summary>After this many failed attempts the message is marked poison (SYSTEM_DESIGN.md §19.2: 10).</summary>
    public int MaxAttempts { get; set; } = 10;

    public TimeSpan BaseBackoff { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(5);
}
