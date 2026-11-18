using Astryx.Contracts.MarketData;
using Astryx.Contracts.Quant;
using Astryx.Modules.Quant.Application.Indicators;
using Astryx.Modules.Quant.Domain;

namespace Astryx.Modules.Quant.Application.Analysis;

/// <summary>
/// Whether a forming bar's volume is volume so far and should be projected to a full session (`Quant:ProjectFormingVolume`).
/// Real providers report volume so far; the fake market's forming bar already carries a full design day (§8 fixtures),
/// so the default follows `MarketData:Provider`.
/// </summary>
public sealed record FormingVolumePolicy(bool Project);

/// <summary>Indicators for strategy rules, from the same history and calculator as the M7 snapshot.</summary>
public sealed class QuantFactsService(IPriceHistoryProvider history, IIndicatorCalculator calculator, RelativeStrengthRanks ranks, IMarketSessionClock sessions, FormingVolumePolicy formingVolume) : IQuantFacts
{
    /// <summary>
    /// Before a quarter of the session has passed, volume is projected as if a quarter had: the opening half hour carries
    /// a large share of the day, so a straight-line projection from the first minutes would wildly overstate it (ASSUMPTION).
    /// </summary>
    public const decimal MinSessionFraction = 0.25m;

    public async Task<QuantFacts?> GetAsync(string symbol, CancellationToken cancellationToken)
    {
        if (await history.GetDailyAsync(symbol, StockAnalysisEngine.LookbackBars, cancellationToken) is not { } daily)
        {
            return null;
        }

        var bars = daily.Bars.Select(b => new Bar(b.SessionDate, b.Open, b.High, b.Low, b.Close, b.Volume)).ToList();
        if (formingVolume.Project && daily.Bars is [.., { Final: false } forming] && await ProjectedVolumeAsync(forming.Volume, cancellationToken) is { } projected)
        {
            // Today's bar is still forming: compare a full-day projection with full-day averages, so an intraday
            // breakout on heavy volume passes the volume test before the close, not only at it.
            bars[^1] = bars[^1] with { Volume = projected };
        }

        var trend = TrendClassifier.Classify(bars.Select(b => b.Close).ToList(), calculator);
        return new QuantFacts(
            daily,
            calculator.Atr(bars).Last,
            calculator.RelativeVolume(bars).Last,
            await ranks.RankAsync(daily.Symbol, cancellationToken),
            trend.State == TrendState.Insufficient ? null : trend.State.ToString().ToLowerInvariant(),
            calculator.Rsi(bars.Select(b => b.Close).ToList()).Last);
    }

    private async Task<long?> ProjectedVolumeAsync(long volumeSoFar, CancellationToken cancellationToken) =>
        await sessions.TodayAsync(cancellationToken) is { } session ? ProjectVolume(volumeSoFar, session, sessions.Now) : null;

    /// <summary>Today's volume so far scaled to a full session, or null outside the session.</summary>
    public static long? ProjectVolume(long volumeSoFar, TradingSession session, DateTimeOffset now)
    {
        if (now <= session.OpenAt || now >= session.CloseAt)
        {
            return null;
        }

        var elapsed = (decimal)(now - session.OpenAt).Ticks / (session.CloseAt - session.OpenAt).Ticks;
        return (long)Math.Round(volumeSoFar / Math.Max(elapsed, MinSessionFraction), MidpointRounding.ToEven);
    }
}
