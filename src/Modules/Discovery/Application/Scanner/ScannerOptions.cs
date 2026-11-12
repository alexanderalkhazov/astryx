namespace Astryx.Modules.Discovery.Application.Scanner;

/// <summary><c>Discovery:Scanner</c> (SYSTEM_DESIGN.md §7.4, §19.2). Universe filters default to the design's Settings ("$10 · $20M").</summary>
public sealed class ScannerOptions
{
    public const string SectionName = "Discovery:Scanner";

    /// <summary>Registers the recurring scan. Off in tests, which run the handler directly.</summary>
    public bool Enabled { get; set; } = true;

    public int IntervalMinutes { get; set; } = 15;

    public string UniverseCode { get; set; } = "default";

    public decimal MinPrice { get; set; } = 10m;

    /// <summary>20-session average volume × last close.</summary>
    public decimal MinAvgDollarVolume { get; set; } = 20_000_000m;

    /// <summary>Signals expire this long after evaluation unless a newer run confirms them (ASSUMPTION).</summary>
    public int SignalLifetimeHours { get; set; } = 24;
}
