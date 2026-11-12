namespace Astryx.Modules.Ai.Application.Jobs;

/// <summary><c>Ai:Jobs</c> (SYSTEM_DESIGN.md §12.8).</summary>
public sealed class AnalysisJobOptions
{
    public const string SectionName = "Ai:Jobs";

    /// <summary>Analyses a user may request per rolling 24 hours.</summary>
    public int DailyQuota { get; set; } = 30;

    /// <summary>An equivalent request within this window returns the existing job.</summary>
    public int DedupeMinutes { get; set; } = 15;

    public int PollAfterMs { get; set; } = 2000;

    public int MaxAttempts { get; set; } = 3;

    /// <summary>Added to the call timeouts to form the lease.</summary>
    public int LeaseGraceSeconds { get; set; } = 30;

    /// <summary>Backoff before attempts 2 and 3; ±20 % jitter is added.</summary>
    public int[] BackoffSeconds { get; set; } = [10, 30, 90];

    /// <summary>Runs <c>Ai:MaxConcurrency</c> worker loops when true (and <c>Workers:Enabled</c>).</summary>
    public bool WorkerEnabled { get; set; } = true;

    public int PollIntervalMs { get; set; } = 2000;
}
