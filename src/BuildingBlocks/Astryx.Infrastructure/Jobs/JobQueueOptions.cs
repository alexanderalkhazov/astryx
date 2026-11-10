namespace Astryx.Infrastructure.Jobs;

/// <summary>Bound from configuration section <c>Jobs</c>.</summary>
public sealed class JobQueueOptions
{
    public int Concurrency { get; set; } = 2;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan ReapInterval { get; set; } = TimeSpan.FromSeconds(30);

    public int DefaultMaxAttempts { get; set; } = 3;

    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Lease = timeout + grace; a lease that outlives this is reclaimed by the reaper.</summary>
    public TimeSpan LeaseGrace { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan BaseBackoff { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(15);
}
