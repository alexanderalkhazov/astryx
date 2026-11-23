using System.Globalization;
using System.Text.Json.Nodes;

namespace Astryx.Modules.Strategies.Application.Rules;

/// <summary>What several strategy rules share: parameter parsing, the §13.4 score weights, level rounding.</summary>
public static class RuleHelpers
{
    public const decimal SetupWeight = 30m, StrengthWeight = 20m, VolumeWeight = 20m, TrendWeight = 15m, RewardWeight = 15m;

    /// <summary>Parameters are JSON numbers or decimal strings ("1.8"), however the JSON node was created.</summary>
    public static decimal Number(JsonObject parameters, string key) =>
        parameters[key] switch
        {
            JsonValue v when v.GetValueKind() == System.Text.Json.JsonValueKind.Number => decimal.Parse(v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture),
            JsonValue v when v.GetValueKind() == System.Text.Json.JsonValueKind.String => decimal.Parse(v.GetValue<string>(), NumberStyles.Number, CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Strategy parameter '{key}' is missing."),
        };

    /// <summary>A buy stop just above a level, rounded up to the dime (as Base Breakout's entry).</summary>
    public static decimal BuyStopAbove(decimal level, decimal buffer) => Math.Ceiling(level * (1m + buffer) * 10m) / 10m;

    public static decimal Cent(decimal value) => Math.Round(value, 2, MidpointRounding.ToEven);

    /// <summary>The §13.4 breakdown and its rounded score.</summary>
    public static (int Score, IReadOnlyDictionary<string, decimal> Breakdown) Score(decimal setup, int rank, decimal volume, string? trend, decimal rewardRisk)
    {
        var breakdown = new Dictionary<string, decimal>
        {
            ["setup"] = SetupWeight * Math.Clamp(setup, 0m, 1m),
            ["relativeStrength"] = StrengthWeight * rank / 99m,
            ["volume"] = VolumeWeight * Math.Clamp(volume, 0m, 1m),
            ["trend"] = TrendWeight * (trend == "uptrend" ? 1m : 0.4m),
            ["rewardRisk"] = RewardWeight * Math.Min(rewardRisk / 3m, 1m),
        };
        return ((int)Math.Round(breakdown.Values.Sum(), MidpointRounding.ToEven), breakdown.ToDictionary(p => p.Key, p => Math.Round(p.Value, 2, MidpointRounding.ToEven)));
    }
}

/// <summary>
/// A rule whose positions also exit on a condition a bracket can't express: time held or an indicator (T-163). Asked
/// once per completed session while a position is open; a reason means "sell at the next open".
/// </summary>
public interface IStrategyExit
{
    /// <summary>The exit reason (for the trade record), or null to keep holding.</summary>
    string? ExitReason(QuantFactsView facts, int sessionsHeld, JsonObject parameters);
}

/// <summary>Closes through the last completed bar, for exit decisions.</summary>
public sealed record QuantFactsView(IReadOnlyList<decimal> Closes);
