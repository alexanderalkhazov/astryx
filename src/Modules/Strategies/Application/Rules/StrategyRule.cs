using System.Text.Json.Nodes;
using Astryx.Contracts.Quant;

namespace Astryx.Modules.Strategies.Application.Rules;

/// <summary>A deterministic signal (SYSTEM_DESIGN.md §13.4): levels, an integer score with its breakdown and a reason.</summary>
/// <param name="Breakdown">Points per component (weight × normalised value), summing to the unrounded score.</param>
public sealed record SignalCandidate(
    string StrategyCode, string Symbol, string SignalType, string OrderType, decimal Entry, decimal Stop, decimal Target, decimal RewardRisk,
    int Score, IReadOnlyDictionary<string, decimal> Breakdown, string Reason, DateOnly AsOfBar);

/// <summary>Why a rule produced no signal, as a stable code (for scanner run statistics).</summary>
public sealed record NoSignal(string Code, string Detail);

/// <summary>A rule's answer: a candidate or the reason there is none.</summary>
public sealed record RuleResult(SignalCandidate? Candidate, NoSignal? Miss)
{
    public static RuleResult Signal(SignalCandidate candidate) => new(candidate, null);

    public static RuleResult None(string code, string detail) => new(null, new NoSignal(code, detail));
}

/// <summary>One implementation per strategy (§7.8). Pure: the same facts and parameters always give the same result.</summary>
public interface IStrategyRule
{
    string Code { get; }

    /// <summary>Implementation version stored on strategy versions (<c>code_version</c>).</summary>
    string CodeVersion { get; }

    RuleResult Evaluate(QuantFacts facts, JsonObject parameters);
}
