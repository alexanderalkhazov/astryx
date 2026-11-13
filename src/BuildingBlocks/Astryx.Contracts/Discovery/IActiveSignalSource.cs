namespace Astryx.Contracts.Discovery;

/// <summary>The highest-scoring active signal for a symbol, as the AI context needs it (SYSTEM_DESIGN.md §12.4).</summary>
public sealed record ActiveSignal(string Type, decimal Score, string Reason, string OrderType, decimal Entry, decimal Stop, decimal Target, DateTimeOffset EvaluatedAt);

/// <summary>Owned by Discovery, read by Ai through Contracts.</summary>
public interface IActiveSignalSource
{
    /// <summary>Null when the symbol has no active signal.</summary>
    Task<ActiveSignal?> GetAsync(string symbol, CancellationToken cancellationToken);
}

/// <summary>A signal by id with what a trade proposal needs (§7.9): levels, lineage and the template copy inputs.</summary>
/// <param name="Status"><c>active</c>, <c>superseded</c>, <c>expired</c> or <c>invalidated</c>.</param>
public sealed record SignalDetail(
    Guid Id, Guid InstrumentId, string Symbol, string Status, string Type, string OrderType, decimal Entry, decimal Stop, decimal Target, decimal Score,
    string Reason, Guid StrategyId, Guid StrategyVersionId, string StrategyName, DateTimeOffset EvaluatedAt, DateTimeOffset ExpiresAt,
    string? RelVolume, string? RelStrength, string? RewardRisk, string? Rsi14);

/// <summary>Owned by Discovery, read by Trading through Contracts.</summary>
public interface ISignalLookup
{
    Task<SignalDetail?> GetAsync(Guid signalId, CancellationToken cancellationToken);
}

public sealed record StrategySignal(Guid Id, string Symbol, string Type, decimal Score, decimal Entry, DateTimeOffset EvaluatedAt);

/// <summary>Owned by Discovery, read by Strategies through Contracts: a strategy's active signals for its detail screen.</summary>
public interface IStrategySignalFeed
{
    /// <summary>Best score first, at most 20.</summary>
    Task<IReadOnlyList<StrategySignal>> ActiveForStrategyAsync(Guid strategyId, CancellationToken cancellationToken);
}
