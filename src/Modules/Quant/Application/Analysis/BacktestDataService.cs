using Astryx.Contracts.MarketData;
using Astryx.Contracts.Quant;
using Astryx.Modules.Quant.Application.Indicators;
using Astryx.Modules.Quant.Domain;

namespace Astryx.Modules.Quant.Application.Analysis;

/// <summary>
/// <see cref="IBacktestData"/>: the same calculator, trend rule and relative-strength score as the live facts
/// (<see cref="QuantFactsService"/>), computed once over each symbol's whole history instead of per request. Wilder
/// ATR and RSI are seeded from the first loaded bar, so early values can differ slightly from a live 300-bar window.
/// </summary>
public sealed class BacktestDataService(IInstrumentDirectory instruments, IPriceHistoryProvider history, IIndicatorCalculator calculator, Microsoft.Extensions.Logging.ILogger<BacktestDataService> logger) : IBacktestData
{
    public async Task<BacktestDataset> LoadAsync(int maxBars, CancellationToken cancellationToken)
    {
        var universe = await instruments.ListActiveEquitiesAsync(cancellationToken);
        var loaded = new List<(InstrumentSummary Instrument, IReadOnlyList<DailyBar> Bars)>(universe.Count);
        foreach (var instrument in universe)
        {
            if (loaded.Count % 100 == 0)
            {
                Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(logger, "Backtest data: {Loaded} of {Total} symbols loaded", loaded.Count, universe.Count);
            }

            if (await history.GetDailyAsync(instrument.Symbol, maxBars, cancellationToken) is { Bars.Count: > 0 } daily)
            {
                loaded.Add((instrument, daily.Bars.Where(b => b.Final).ToList()));
            }
        }

        // Relative strength is cross-sectional: score every symbol on every session, then rank per session.
        var scores = loaded.Select(l => RelativeStrengthScores(l.Bars)).ToList();
        var ranks = loaded.Select(l => new int?[l.Bars.Count]).ToList();
        var byDate = new Dictionary<DateOnly, List<(int Series, int Index, decimal Score)>>();
        for (var s = 0; s < loaded.Count; s++)
        {
            for (var i = 0; i < loaded[s].Bars.Count; i++)
            {
                if (scores[s][i] is { } score)
                {
                    var date = loaded[s].Bars[i].SessionDate;
                    if (!byDate.TryGetValue(date, out var list))
                    {
                        byDate[date] = list = [];
                    }

                    list.Add((s, i, score));
                }
            }
        }

        foreach (var (_, entries) in byDate)
        {
            var ranked = RelativeStrength.Rank(entries.Select((e, k) => (Key: k.ToString(System.Globalization.CultureInfo.InvariantCulture), e.Score)).ToDictionary(x => x.Key, x => x.Score));
            for (var k = 0; k < entries.Count; k++)
            {
                ranks[entries[k].Series][entries[k].Index] = ranked[k.ToString(System.Globalization.CultureInfo.InvariantCulture)];
            }
        }

        var series = loaded.Select((l, s) =>
        {
            var bars = l.Bars.Select(b => new Bar(b.SessionDate, b.Open, b.High, b.Low, b.Close, b.Volume)).ToList();
            var closes = bars.Select(b => b.Close).ToList();
            var trend = TrendClassifier.Series(closes, calculator).Select(t => t == TrendState.Insufficient ? null : t.ToString().ToLowerInvariant()).ToList();
            return new BacktestSeries(
                l.Instrument.Id, l.Instrument.Symbol, l.Instrument.Sector, l.Bars,
                Values(calculator.Atr(bars), bars.Count), Values(calculator.RelativeVolume(bars), bars.Count), Values(calculator.Rsi(closes), bars.Count), trend, ranks[s]);
        }).ToList();

        var sessions = byDate.Keys.Concat(loaded.SelectMany(l => l.Bars.Select(b => b.SessionDate))).Distinct().Order().ToList();
        return new BacktestDataset(series, sessions);
    }

    /// <summary>
    /// The §13.3 score at every bar with a year of history before it: <c>0.4·ROC63 + 0.2·(ROC126 + ROC189 + ROC252)</c>,
    /// the same weights as <see cref="RelativeStrength.Score"/>.
    /// </summary>
    private static decimal?[] RelativeStrengthScores(IReadOnlyList<DailyBar> bars)
    {
        var scores = new decimal?[bars.Count];
        for (var i = RelativeStrength.MinBars - 1; i < bars.Count; i++)
        {
            var (c, c63, c126, c189, c252) = (bars[i].Close, bars[i - 63].Close, bars[i - 126].Close, bars[i - 189].Close, bars[i - 252].Close);
            if (c63 > 0m && c126 > 0m && c189 > 0m && c252 > 0m)
            {
                scores[i] = 0.4m * (c / c63 - 1m) + 0.2m * (c / c126 - 1m) + 0.2m * (c / c189 - 1m) + 0.2m * (c / c252 - 1m);
            }
        }

        return scores;
    }

    /// <summary>An indicator's values, or an all-null series when there were too few bars to compute it.</summary>
    private static IReadOnlyList<decimal?> Values(IndicatorSeries series, int count) =>
        series.Values.Count == count ? series.Values : new decimal?[count];
}
