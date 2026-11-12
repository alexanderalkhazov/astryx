namespace Astryx.Modules.MarketData.Domain;

/// <summary>Row in <c>market.price_bars</c>: unadjusted OHLCV history for every timeframe (SYSTEM_DESIGN.md §10.4, §20.7).</summary>
public sealed class PriceBar
{
    public Guid InstrumentId { get; set; }

    public required string Timeframe { get; set; }

    /// <summary>UTC start of the bar; daily bars use the session open instant.</summary>
    public DateTimeOffset BarStart { get; set; }

    /// <summary>Exchange-local trading date.</summary>
    public DateOnly SessionDate { get; set; }

    public decimal Open { get; set; }

    public decimal High { get; set; }

    public decimal Low { get; set; }

    public decimal Close { get; set; }

    public long Volume { get; set; }

    /// <summary>False while the bar is still forming.</summary>
    public bool IsFinal { get; set; }

    public required string Source { get; set; }

    public DateTimeOffset IngestedAt { get; set; }
}

public static class Timeframe
{
    public const string OneMinute = "1m";
    public const string FiveMinutes = "5m";
    public const string FifteenMinutes = "15m";
    public const string OneHour = "1h";
    public const string OneDay = "1d";
}
