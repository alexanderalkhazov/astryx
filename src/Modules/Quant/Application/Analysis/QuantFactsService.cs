using Astryx.Contracts.MarketData;
using Astryx.Contracts.Quant;
using Astryx.Modules.Quant.Application.Indicators;
using Astryx.Modules.Quant.Domain;

namespace Astryx.Modules.Quant.Application.Analysis;

/// <summary>Indicators for strategy rules, from the same history and calculator as the M7 snapshot.</summary>
public sealed class QuantFactsService(IPriceHistoryProvider history, IIndicatorCalculator calculator, RelativeStrengthRanks ranks) : IQuantFacts
{
    public async Task<QuantFacts?> GetAsync(string symbol, CancellationToken cancellationToken)
    {
        if (await history.GetDailyAsync(symbol, StockAnalysisEngine.LookbackBars, cancellationToken) is not { } daily)
        {
            return null;
        }

        var bars = daily.Bars.Select(b => new Bar(b.SessionDate, b.Open, b.High, b.Low, b.Close, b.Volume)).ToList();
        var trend = TrendClassifier.Classify(bars.Select(b => b.Close).ToList(), calculator);
        return new QuantFacts(
            daily,
            calculator.Atr(bars).Last,
            calculator.RelativeVolume(bars).Last,
            await ranks.RankAsync(daily.Symbol, cancellationToken),
            trend.State == TrendState.Insufficient ? null : trend.State.ToString().ToLowerInvariant(),
            calculator.Rsi(bars.Select(b => b.Close).ToList()).Last);
    }
}
