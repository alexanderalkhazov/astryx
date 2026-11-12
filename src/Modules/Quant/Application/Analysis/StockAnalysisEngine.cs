using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Astryx.Contracts.MarketData;
using Astryx.Modules.Quant.Application.Indicators;
using Astryx.Modules.Quant.Domain;

namespace Astryx.Modules.Quant.Application.Analysis;

/// <summary>Data quality of a snapshot (SYSTEM_DESIGN.md §13.5).</summary>
public static class DataQuality
{
    public const string Ok = "ok";
    public const string Gaps = "gaps";
    public const string Stale = "stale";
    public const string Insufficient = "insufficient";

    /// <summary>More missing sessions than this in the lookback make the data unusable for signals.</summary>
    public const int MaxGaps = 2;
}

/// <summary>
/// The M7 snapshot: indicator values as presentation strings (4 dp; RSI 1 dp; relative volume 2 dp; RS rank 1–99),
/// null where history is too short, with the names of those in <see cref="Insufficient"/>.
/// </summary>
public sealed record IndicatorSnapshot(
    string Symbol, string Interval, DateOnly AsOfBar, int BarCount, string DataQuality, IReadOnlyDictionary<string, string?> Values, IReadOnlyList<string> Insufficient);

/// <summary>
/// Turns a daily history into an <see cref="IndicatorSnapshot"/> (SYSTEM_DESIGN.md §7.6, §13). Pure: the same
/// history and RS rank always give the same snapshot.
/// </summary>
public sealed class StockAnalysisEngine(IIndicatorCalculator calculator)
{
    /// <summary>Bump when a formula or a default parameter changes, so cached snapshots are recomputed.</summary>
    public const string CodeVersion = "quant-v1";

    /// <summary>Bars fetched for a snapshot: SMA200 for the trend plus a slope window, with room to spare.</summary>
    public const int LookbackBars = 300;

    /// <summary>Fewer bars than this and no core indicator exists: M7 answers 422 <c>insufficient_history</c>.</summary>
    public const int MinBars = 21;

    /// <summary>Value names in response order (jsonb storage does not keep key order).</summary>
    public static readonly IReadOnlyList<string> ValueNames =
    [
        "sma20", "sma50", "ema20", "rsi14", "macd", "macdSignal", "macdHist", "atr14", "relVolume20",
        "momentum20Pct", "changePct1d", "volatility20Ann", "relStrength", "trend",
    ];

    public const string Parameters = "sma=20,50;ema=20;rsi=14;macd=12,26,9;atr=14;relvol=20;roc=20;vol=20;trend=50,200/20,50;rs=63,126,189,252";

    /// <summary>Identifies the parameter set and code version in the snapshot cache.</summary>
    public static string ParamsHash { get; } =
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{CodeVersion}|{Parameters}")))[..16];

    public IndicatorSnapshot Analyze(DailyHistory history, int? relStrengthRank)
    {
        var bars = history.Bars.Select(b => new Bar(b.SessionDate, b.Open, b.High, b.Low, b.Close, b.Volume)).ToList();
        var closes = bars.Select(b => b.Close).ToList();
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        var insufficient = new List<string>();

        void Put(string name, IndicatorSeries series, Func<decimal, string> format)
        {
            if (series.Status == IndicatorStatus.Insufficient || series.Last is not { } last)
            {
                values[name] = null;
                insufficient.Add(name);
            }
            else
            {
                values[name] = format(last);
            }
        }

        Put("sma20", calculator.Sma(closes, 20), D4);
        Put("sma50", calculator.Sma(closes, 50), D4);
        Put("ema20", calculator.Ema(closes, 20), D4);
        Put("rsi14", calculator.Rsi(closes), v => Fixed(v, 1));
        var macd = calculator.Macd(closes);
        Put("macd", macd.Macd, D4);
        Put("macdSignal", macd.Signal, D4);
        Put("macdHist", macd.Histogram, D4);
        Put("atr14", calculator.Atr(bars), D4);
        Put("relVolume20", calculator.RelativeVolume(bars), v => Fixed(v, 2));
        Put("momentum20Pct", calculator.Momentum(closes), D4);
        Put("changePct1d", calculator.Momentum(closes, 1), D4);
        Put("volatility20Ann", calculator.Volatility(closes), D4);

        if (relStrengthRank is { } rank)
        {
            values["relStrength"] = rank.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            values["relStrength"] = null;
            insufficient.Add("relStrength");
        }

        var trend = TrendClassifier.Classify(closes, calculator);
        values["trend"] = trend.State == TrendState.Insufficient ? null : trend.State.ToString().ToLowerInvariant();
        if (trend.State == TrendState.Insufficient)
        {
            insufficient.Add("trend");
        }

        return new IndicatorSnapshot(history.Symbol, "1d", bars[^1].SessionDate, bars.Count, Quality(history, insufficient), values, insufficient);
    }

    /// <summary>
    /// Stale first (it blocks proposals), then insufficient (a missing indicator, or more than two gaps), then gaps,
    /// else ok (§13.5).
    /// </summary>
    public static string Quality(DailyHistory history, IReadOnlyCollection<string> insufficient) =>
        history.Stale ? DataQuality.Stale
        : insufficient.Count > 0 || history.MissingSessions.Count > DataQuality.MaxGaps ? DataQuality.Insufficient
        : history.MissingSessions.Count > 0 ? DataQuality.Gaps
        : DataQuality.Ok;

    private static string D4(decimal value) => Fixed(value, 4);

    private static string Fixed(decimal value, int places) =>
        Math.Round(value, places, MidpointRounding.ToEven).ToString("F" + places.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
}
