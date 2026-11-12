using System.Globalization;
using System.Text.Json.Nodes;
using Astryx.Contracts.Quant;

namespace Astryx.Modules.Strategies.Application.Rules;

/// <summary>
/// Base Breakout (SYSTEM_DESIGN.md §13.4.1, partly answers Q-09). The base is the <c>baseWeeks × 5</c> sessions
/// before today. A signal needs: a base no deeper than 20 %, today's high above the base high, today's volume at least
/// <c>volumeMultiple</c> × the 20-session average, a relative-strength rank of at least <c>minRelStrength</c>, and no
/// downtrend. Levels: buy stop 0.3 % above the base high (rounded up to the dime), stop <c>stopAtr</c> × ATR(14) below
/// entry, target at the base high plus twice the base depth.
/// </summary>
public sealed class BaseBreakoutRule : IStrategyRule
{
    public const string RuleCode = "base_breakout";

    /// <summary>Deepest base accepted, as a fraction of the base high.</summary>
    public const decimal MaxDepth = 0.20m;

    /// <summary>The buy stop sits this far above the base high.</summary>
    public const decimal EntryBuffer = 0.003m;

    // §13.4 default breakout weights.
    private const decimal SetupWeight = 30m, StrengthWeight = 20m, VolumeWeight = 20m, TrendWeight = 15m, RewardWeight = 15m;

    public string Code => RuleCode;

    public string CodeVersion => $"{RuleCode}@1";

    public RuleResult Evaluate(QuantFacts facts, JsonObject parameters)
    {
        var baseWeeks = (int)Number(parameters, "baseWeeks");
        var volumeMultiple = Number(parameters, "volumeMultiple");
        var minRelStrength = Number(parameters, "minRelStrength");
        var stopAtr = Number(parameters, "stopAtr");

        var bars = facts.History.Bars;
        var sessions = baseWeeks * 5;
        if (bars.Count < sessions + 1 || facts.Atr14 is not { } atr || facts.RelVolume20 is not { } relVolume || facts.Trend is null)
        {
            return RuleResult.None("insufficient_history", $"Needs {sessions + 1} bars, ATR(14), relative volume and a trend.");
        }

        var today = bars[^1];
        var window = bars.Skip(bars.Count - 1 - sessions).Take(sessions).ToList();
        var baseHigh = window.Max(b => b.High);
        var baseLow = window.Min(b => b.Low);
        var depth = (baseHigh - baseLow) / baseHigh;
        if (depth > MaxDepth)
        {
            return RuleResult.None("base_too_deep", $"Base depth {depth:P1} exceeds {MaxDepth:P0}.");
        }

        if (today.High <= baseHigh)
        {
            return RuleResult.None("no_breakout", $"Today's high {today.High} has not cleared the base high {baseHigh}.");
        }

        if (relVolume < volumeMultiple)
        {
            return RuleResult.None("volume_too_low", $"Volume is {relVolume:F2}× average; needs {volumeMultiple}×.");
        }

        if (facts.RelStrengthRank is not { } rank || rank < minRelStrength)
        {
            return RuleResult.None("weak_relative_strength", $"Relative strength {facts.RelStrengthRank?.ToString(CultureInfo.InvariantCulture) ?? "unknown"} is below {minRelStrength}.");
        }

        if (facts.Trend == "downtrend")
        {
            return RuleResult.None("downtrend", "Breakouts are not taken in a downtrend.");
        }

        var entry = Math.Ceiling(baseHigh * (1m + EntryBuffer) * 10m) / 10m;
        var stop = Math.Round(entry - stopAtr * atr, 2, MidpointRounding.ToEven);
        var target = Math.Round(baseHigh + 2m * (baseHigh - baseLow), 2, MidpointRounding.ToEven);
        var rewardRisk = (target - entry) / (entry - stop);

        // Setup quality: a tight base that closes in its upper half.
        var tightness = 1m - depth / MaxDepth;
        var midpoint = (baseHigh + baseLow) / 2m;
        var closeStrength = (decimal)window.Count(b => b.Close >= midpoint) / window.Count;
        var setup = (tightness + closeStrength) / 2m;

        var breakdown = new Dictionary<string, decimal>
        {
            ["setup"] = SetupWeight * setup,
            ["relativeStrength"] = StrengthWeight * rank / 99m,
            ["volume"] = VolumeWeight * Math.Min(relVolume / 3m, 1m),
            ["trend"] = TrendWeight * (facts.Trend == "uptrend" ? 1m : 0.4m),
            ["rewardRisk"] = RewardWeight * Math.Min(rewardRisk / 3m, 1m),
        };
        var score = (int)Math.Round(breakdown.Values.Sum(), MidpointRounding.ToEven);

        var reason = string.Create(CultureInfo.InvariantCulture, $"Cleared a {baseWeeks}-week base at ${baseHigh:F2} on {Math.Round(relVolume, 1, MidpointRounding.ToEven):F1}× average volume.");
        return RuleResult.Signal(new SignalCandidate(
            RuleCode, facts.History.Symbol, "breakout", "buy_stop", entry, stop, target, Math.Round(rewardRisk, 2, MidpointRounding.ToEven),
            score, breakdown.ToDictionary(p => p.Key, p => Math.Round(p.Value, 2, MidpointRounding.ToEven)), reason, today.SessionDate));
    }

    /// <summary>Parameters are JSON numbers or decimal strings ("1.8"), however the JSON node was created.</summary>
    private static decimal Number(JsonObject parameters, string key) =>
        parameters[key] switch
        {
            JsonValue v when v.GetValueKind() == System.Text.Json.JsonValueKind.Number => decimal.Parse(v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture),
            JsonValue v when v.GetValueKind() == System.Text.Json.JsonValueKind.String => decimal.Parse(v.GetValue<string>(), NumberStyles.Number, CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Strategy parameter '{key}' is missing."),
        };
}
