using System.Globalization;
using System.Text.Json.Nodes;
using Astryx.Contracts.Quant;
using static Astryx.Modules.Strategies.Application.Rules.RuleHelpers;

namespace Astryx.Modules.Strategies.Application.Rules;

/// <summary>
/// Trend Pullback (<c>trend_pullback@1</c>, rules-draft §2, approved 2026-11-23): in a long uptrend (rising SMA50, close
/// above SMA200), RSI(14) reset to <c>rsiReset</c> or lower within 5 sessions, today's low within one ATR of the SMA50
/// with the close above it and above yesterday's high (the turn), and a pullback no deeper than 12 % from the 20-day
/// high. Buy stop above today's high, stop the lower of the ATR stop and just under the SMA50, target the 20-day high
/// (needs R:R ≥ 1.5).
/// </summary>
public sealed class TrendPullbackRule(IIndicatorMath math) : IStrategyRule
{
    public const string RuleCode = "trend_pullback";
    public const decimal MaxDepth = 0.12m;
    public const decimal MinRewardRisk = 1.5m;

    public string Code => RuleCode;

    public string CodeVersion => $"{RuleCode}@1";

    public RuleResult Evaluate(QuantFacts facts, JsonObject parameters)
    {
        var smaPeriod = (int)Number(parameters, "smaPeriod");
        var rsiReset = Number(parameters, "rsiReset");
        var stopAtr = Number(parameters, "stopAtr");
        var bars = facts.History.Bars;
        if (bars.Count < 200 || facts.Atr14 is not { } atr)
        {
            return RuleResult.None("insufficient_history", "Needs 200 bars and ATR(14).");
        }

        var closes = bars.Select(b => b.Close).ToList();
        var last = closes.Count - 1;
        var sma = math.Sma(closes, smaPeriod);
        var sma200 = math.Sma(closes, 200);
        var rsi = math.Rsi(closes, 14);
        if (sma[last] is not { } smaNow || sma[last - 10] is not { } smaThen || sma200[last] is not { } longSma || rsi[last] is not { } rsiNow)
        {
            return RuleResult.None("insufficient_history", "Moving averages and RSI need more history.");
        }

        var today = bars[^1];
        if (smaNow <= smaThen)
        {
            return RuleResult.None("sma_not_rising", $"The {smaPeriod}-day average is not rising.");
        }

        if (today.Close <= longSma)
        {
            return RuleResult.None("below_sma200", "Close is below the 200-day average.");
        }

        var recentRsi = Enumerable.Range(last - 4, 5).Select(i => rsi[i]).OfType<decimal>().ToList();
        var minRsi = recentRsi.Count > 0 ? recentRsi.Min() : rsiNow;
        if (minRsi > rsiReset)
        {
            return RuleResult.None("rsi_not_reset", $"RSI has not dipped to {rsiReset} in the last 5 sessions (low {minRsi:F1}).");
        }

        if (today.Low > smaNow + atr || today.Close <= smaNow)
        {
            return RuleResult.None("not_at_sma", $"Today did not test the {smaPeriod}-day average and close above it.");
        }

        if (today.Close <= bars[^2].High)
        {
            return RuleResult.None("no_turn", "Today did not close above yesterday's high.");
        }

        var high20 = bars.Skip(bars.Count - 20).Max(b => b.High);
        var recentLow = bars.Skip(bars.Count - 5).Min(b => b.Low);
        var depth = (high20 - recentLow) / high20;
        if (depth > MaxDepth)
        {
            return RuleResult.None("pullback_too_deep", $"Pullback of {depth:P1} is deeper than {MaxDepth:P0}.");
        }

        var entry = BuyStopAbove(today.High, 0.002m);
        var stop = Cent(Math.Min(entry - stopAtr * atr, smaNow - 0.25m * atr));
        var target = Cent(high20);
        var rewardRisk = target > entry && entry > stop ? (target - entry) / (entry - stop) : 0m;
        if (rewardRisk < MinRewardRisk)
        {
            return RuleResult.None("reward_too_small", $"The 20-day high gives R:R {rewardRisk:F2}; needs {MinRewardRisk}.");
        }

        var setup = 0.5m * (1m - depth / MaxDepth) + 0.5m * Math.Min((rsiNow - minRsi) / 20m, 1m);
        var (score, breakdown) = Score(setup, facts.RelStrengthRank ?? 50, (facts.RelVolume20 ?? 1m) / 3m, facts.Trend, rewardRisk);
        var reason = string.Create(CultureInfo.InvariantCulture, $"Pulled back {depth * 100m:F1}% to the {smaPeriod}-day average; RSI reset to {minRsi:F0} and turned up.");
        return RuleResult.Signal(new SignalCandidate(RuleCode, facts.History.Symbol, "pullback", "buy_stop", entry, stop, target, Math.Round(rewardRisk, 2), score, breakdown, reason, today.SessionDate));
    }
}
