using System.Globalization;
using System.Text.Json.Nodes;
using Astryx.Contracts.Quant;
using static Astryx.Modules.Strategies.Application.Rules.RuleHelpers;

namespace Astryx.Modules.Strategies.Application.Rules;

/// <summary>
/// RSI(2) Mean Reversion (<c>rsi2_mean_reversion@1</c>, rules-draft §3, approved 2026-11-23): in a long uptrend (close
/// above SMA200), a 2-day RSI below <c>entryBelow</c>, not down more than 15 % over 5 sessions, and at least $50M a day
/// traded. A limit 0.5 % under the close for the next session, a wide 2.5 × ATR stop, and a target at the 5-day average.
/// Exits early when RSI(2) rises above <c>exitAbove</c> or after <c>maxHoldDays</c> sessions.
/// </summary>
public sealed class Rsi2MeanReversionRule(IIndicatorMath math) : IStrategyRule, IStrategyExit
{
    public const string RuleCode = "rsi2_mean_reversion";
    public const decimal MaxFiveDayDrop = 0.15m;
    public const decimal MinDollarVolume = 50_000_000m;
    public const decimal StopAtr = 2.5m;

    public string Code => RuleCode;

    public string CodeVersion => $"{RuleCode}@1";

    public RuleResult Evaluate(QuantFacts facts, JsonObject parameters)
    {
        var entryBelow = Number(parameters, "entryBelow");
        var bars = facts.History.Bars;
        if (bars.Count < 200 || facts.Atr14 is not { } atr)
        {
            return RuleResult.None("insufficient_history", "Needs 200 bars and ATR(14).");
        }

        var closes = bars.Select(b => b.Close).ToList();
        var last = closes.Count - 1;
        var close = closes[last];
        if (math.Sma(closes, 200)[last] is not { } sma200 || math.Rsi(closes, 2)[last] is not { } rsi2 || math.Sma(closes, 5)[last] is not { } sma5)
        {
            return RuleResult.None("insufficient_history", "Averages and RSI(2) need more history.");
        }

        if (close <= sma200)
        {
            return RuleResult.None("below_sma200", "Mean reversion is only taken above the 200-day average.");
        }

        if (rsi2 >= entryBelow)
        {
            return RuleResult.None("rsi2_not_oversold", $"RSI(2) {rsi2:F1} is not below {entryBelow}.");
        }

        if (close / closes[last - 5] - 1m < -MaxFiveDayDrop)
        {
            return RuleResult.None("crash_filter", $"Down more than {MaxFiveDayDrop:P0} in 5 sessions: likely news, not a dip.");
        }

        var dollarVolume = bars.Skip(bars.Count - 20).Average(b => b.Close * b.Volume);
        if (dollarVolume < MinDollarVolume)
        {
            return RuleResult.None("illiquid", $"Average dollar volume {dollarVolume:N0} is under {MinDollarVolume:N0}.");
        }

        var entry = Cent(close * 0.995m);
        var stop = Cent(entry - StopAtr * atr);
        var target = Cent(sma5);
        if (target <= entry || stop <= 0m)
        {
            return RuleResult.None("target_below_entry", "The 5-day average is not above the entry.");
        }

        var rewardRisk = (target - entry) / (entry - stop);
        var (score, breakdown) = Score((entryBelow - rsi2) / entryBelow, facts.RelStrengthRank ?? 50, 0.5m, facts.Trend, rewardRisk);
        var reason = string.Create(CultureInfo.InvariantCulture, $"2-day RSI {rsi2:F1} in a long-term uptrend: buying the dip.");
        return RuleResult.Signal(new SignalCandidate(RuleCode, facts.History.Symbol, "pullback", "limit", entry, stop, target, Math.Round(rewardRisk, 2), score, breakdown, reason, bars[^1].SessionDate));
    }

    public string? ExitReason(QuantFactsView facts, int sessionsHeld, JsonObject parameters)
    {
        if (sessionsHeld >= (int)Number(parameters, "maxHoldDays"))
        {
            return "time_exit";
        }

        var rsi2 = facts.Closes.Count > 2 ? math.Rsi(facts.Closes, 2)[^1] : null;
        return rsi2 > Number(parameters, "exitAbove") ? "rsi_exit" : null;
    }
}
