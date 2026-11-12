using Astryx.Contracts.Quant;

namespace Astryx.Contracts.Strategies;

/// <summary>A strategy rule's signal for one symbol (SYSTEM_DESIGN.md §13.4). Levels and score are deterministic.</summary>
/// <param name="Breakdown">Points per component (weight × normalised value).</param>
public sealed record StrategySignal(
    string SignalType, string OrderType, decimal Entry, decimal Stop, decimal Target, decimal RewardRisk,
    int Score, IReadOnlyDictionary<string, decimal> Breakdown, string Reason);

/// <summary>One published strategy version evaluated against one symbol: a signal, or the code of the condition that failed.</summary>
public sealed record StrategyEvaluation(Guid StrategyId, Guid StrategyVersionId, string StrategyCode, string StrategyName, int Version, StrategySignal? Signal, string? MissCode);

/// <summary>Owned by Strategies, used by the Discovery scanner through Contracts (§7.4, §7.8).</summary>
public interface IStrategySignals
{
    /// <summary>Every active strategy with an implemented rule, evaluated against the facts.</summary>
    Task<IReadOnlyList<StrategyEvaluation>> EvaluateAsync(QuantFacts facts, CancellationToken cancellationToken);
}
