namespace Astryx.Modules.MarketData.Application.Ingestion;

/// <summary><c>MarketData:Ingestion</c> settings (SYSTEM_DESIGN.md §16.3).</summary>
public sealed class IngestionOptions
{
    public const string SectionName = "MarketData:Ingestion";

    /// <summary>Registers the recurring sync schedules. Integration tests turn it off and call handlers directly.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Provider request budget (token bucket). The fake provider is generous; a vendor sets its real limit.</summary>
    public int RequestsPerMinute { get; set; } = 6000;

    /// <summary>Daily history fetched for an instrument that has none yet.</summary>
    public int BackfillDays { get; set; } = 730;

    /// <summary>A quote moving more than this fraction from the previous close is treated as a bad tick and dropped.</summary>
    public decimal MaxQuoteMoveFromPrevClose { get; set; } = 0.5m;
}
