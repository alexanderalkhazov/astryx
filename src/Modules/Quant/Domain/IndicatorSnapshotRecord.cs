namespace Astryx.Modules.Quant.Domain;

/// <summary>
/// Row in <c>market.indicator_snapshots</c> (SYSTEM_DESIGN.md §10.4): a cache, reproducible from bars. Keyed by the
/// last bar used and the hash of the parameters and code version.
/// </summary>
public sealed class IndicatorSnapshotRecord
{
    /// <summary>Snapshots kept per instrument and timeframe; older ones are pruned on insert (§18 retention).</summary>
    public const int Keep = 5;

    public Guid Id { get; set; }

    /// <summary>Refers to <c>market.instruments</c> (owned by MarketData; no cross-module foreign key, §9.2).</summary>
    public Guid InstrumentId { get; set; }

    public required string Timeframe { get; set; }

    public DateTimeOffset AsOfBar { get; set; }

    public required string ParamsHash { get; set; }

    /// <summary>JSON: indicator name → decimal string, or null when insufficient.</summary>
    public required string Values { get; set; }

    public required string DataQuality { get; set; }

    public DateTimeOffset ComputedAt { get; set; }
}
