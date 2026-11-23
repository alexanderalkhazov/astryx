using Astryx.Contracts.MarketData;

namespace Astryx.Contracts.Quant;

/// <summary>
/// One symbol's daily history with indicator series aligned to its bars (T-160). Every value at index <c>i</c> uses bars
/// <c>0..i</c> only, so a backtest that reads index <c>i</c> on session <c>i</c> cannot see the future.
/// </summary>
/// <param name="RelStrengthRank">The 1–99 percentile of the §13.3 relative-strength score across the universe on that session.</param>
public sealed record BacktestSeries(
    Guid InstrumentId, string Symbol, string? Sector, IReadOnlyList<DailyBar> Bars,
    IReadOnlyList<decimal?> Atr14, IReadOnlyList<decimal?> RelVolume20, IReadOnlyList<decimal?> Rsi14, IReadOnlyList<string?> Trend, IReadOnlyList<int?> RelStrengthRank);

/// <summary>The universe's series and every session any of them traded, ascending.</summary>
public sealed record BacktestDataset(IReadOnlyList<BacktestSeries> Series, IReadOnlyList<DateOnly> Sessions);

/// <summary>Owned by Quant, used by the Strategies backtester through Contracts (T-160).</summary>
public interface IBacktestData
{
    /// <summary>The active universe's last <paramref name="maxBars"/> split-adjusted daily bars and their series.</summary>
    Task<BacktestDataset> LoadAsync(int maxBars, CancellationToken cancellationToken);
}
