namespace Astryx.Contracts.Quant;

/// <summary>
/// The M7 daily snapshot (SYSTEM_DESIGN.md §11.4): values as presentation strings, null where history is too short.
/// <paramref name="DataQuality"/> is <c>ok</c>, <c>gaps</c>, <c>stale</c> or <c>insufficient</c>.
/// </summary>
public sealed record IndicatorReadout(string Symbol, DateOnly? AsOfBar, int BarCount, string DataQuality, IReadOnlyList<DateOnly> MissingSessions, IReadOnlyDictionary<string, string?> Values);

/// <summary>Owned by Quant, read by Ai through Contracts.</summary>
public interface IIndicatorSnapshots
{
    /// <summary>
    /// Null for an unknown symbol. Too little history gives <c>insufficient</c> with no values, rather than an error.
    /// </summary>
    Task<IndicatorReadout?> GetDailyAsync(string symbol, CancellationToken cancellationToken);
}
