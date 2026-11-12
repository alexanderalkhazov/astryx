namespace Astryx.Contracts.MarketData;

/// <summary>Entry, stop and target drawn on a chart (SYSTEM_DESIGN.md §11.4 M6 <c>levels</c>).</summary>
public sealed record ChartLevels(decimal Entry, decimal Stop, decimal Target);

/// <summary>
/// The active signal's levels for a symbol, implemented by Discovery once signals exist (M3). Without an
/// implementation, bars carry no levels.
/// </summary>
public interface IChartLevelsSource
{
    Task<ChartLevels?> GetLevelsAsync(string symbol, CancellationToken cancellationToken);
}
