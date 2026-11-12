namespace Astryx.Modules.Quant.Domain;

/// <summary>One daily (or intraday) bar as the quant engine sees it: decimals throughout (SYSTEM_DESIGN.md §13.2).</summary>
public sealed record Bar(DateOnly SessionDate, decimal Open, decimal High, decimal Low, decimal Close, long Volume);

public enum IndicatorStatus
{
    Ok,

    /// <summary>Fewer bars than the indicator needs: every value is null. Never a partial series (§13.5).</summary>
    Insufficient,
}

/// <summary>
/// An indicator aligned to its input: <c>Values[i]</c> belongs to bar <c>i</c>, null during warm-up.
/// </summary>
/// <param name="MinBars">Bars needed for the first value (§13.3).</param>
/// <param name="ReliableFrom">
/// First index whose value no longer depends noticeably on the seed (EMA-based: 2n, §13.3); equal to the first value
/// for simple windows.
/// </param>
public sealed record IndicatorSeries(IReadOnlyList<decimal?> Values, IndicatorStatus Status, int MinBars, int ReliableFrom)
{
    public decimal? Last => Values.Count == 0 ? null : Values[^1];

    public static IndicatorSeries Insufficient(int count, int minBars) => new(new decimal?[count], IndicatorStatus.Insufficient, minBars, minBars - 1);
}

/// <summary>MACD line, signal line and histogram, each aligned to the closes.</summary>
public sealed record MacdSeries(IndicatorSeries Macd, IndicatorSeries Signal, IndicatorSeries Histogram)
{
    public IndicatorStatus Status => Histogram.Status;
}

public enum TrendState
{
    Uptrend,
    Downtrend,
    Sideways,
    Insufficient,
}

/// <param name="ShortHistory">Fewer than 200 bars: classified with the SMA20/SMA50 fallback (§13.3).</param>
public sealed record Trend(TrendState State, bool ShortHistory);
