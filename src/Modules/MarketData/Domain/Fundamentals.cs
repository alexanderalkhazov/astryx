namespace Astryx.Modules.MarketData.Domain;

/// <summary>Row in <c>market.fundamentals</c>: the latest fundamentals per instrument (SYSTEM_DESIGN.md §10.4).</summary>
public sealed class Fundamentals
{
    public Guid InstrumentId { get; set; }

    public DateTimeOffset AsOf { get; set; }

    public decimal? Beta { get; set; }

    public decimal? PeRatio { get; set; }

    public decimal? EpsTtm { get; set; }

    public DateOnly? NextEarningsDate { get; set; }

    /// <summary>JSON object with every other provider field.</summary>
    public string Data { get; set; } = "{}";

    public required string Source { get; set; }
}
