namespace Astryx.Modules.MarketData.Domain;

/// <summary>Row in <c>market.quotes</c>: the latest quote per instrument. Ticks are not stored (SYSTEM_DESIGN.md §10.4).</summary>
public sealed class Quote
{
    public Guid InstrumentId { get; set; }

    public decimal Last { get; set; }

    /// <summary>Unavailable on some free tiers.</summary>
    public decimal? Bid { get; set; }

    public decimal? Ask { get; set; }

    public decimal Open { get; set; }

    public decimal High { get; set; }

    public decimal Low { get; set; }

    public decimal PrevClose { get; set; }

    public long Volume { get; set; }

    /// <summary>Provider timestamp of the last trade or quote.</summary>
    public DateTimeOffset AsOf { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    public required string Source { get; set; }

    public string Quality { get; set; } = QuoteQuality.Delayed;

    /// <summary>Monotonic per instrument; part of the risk assessment inputs hash.</summary>
    public long QuoteSeq { get; set; }
}

public static class QuoteQuality
{
    public const string Realtime = "realtime";
    public const string Delayed = "delayed";
    public const string Eod = "eod";
    public const string Synthetic = "synthetic";
}
