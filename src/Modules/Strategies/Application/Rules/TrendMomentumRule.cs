using System.Globalization;
using System.Text.Json.Nodes;
using Astryx.Contracts.Quant;
using static Astryx.Modules.Strategies.Application.Rules.RuleHelpers;

namespace Astryx.Modules.Strategies.Application.Rules;

/// <summary>
/// Trend Momentum (<c>trend_momentum@1</c>, docs/strategies/rules-draft.md §1, approved 2026-11-23): a leader above a
/// rising 21-day EMA, MACD above its signal with the histogram rising, relative strength at least
/// <c>minRelStrength</c>, an uptrend, and the close within 8 % of the 52-week high. Buy stop 0.2 % above today's high,
/// stop <c>stopAtr</c> × ATR below entry, target 3R.
/// </summary>
public sealed class TrendMomentumRule(IIndicatorMath math) : IStrategyRule
{
    public const string RuleCode = "trend_momentum";
    public const decimal MaxFromHigh = 0.08m;
    public const int SlopeBars = 5;

    public string Code => RuleCode;

    public string CodeVersion => $"{RuleCode}@1";

    public RuleResult Evaluate(QuantFacts facts, JsonObject parameters)
    {
        var emaPeriod = (int)Number(parameters, "emaPeriod");
        var minRelStrength = Number(parameters, "minRelStrength");
        var stopAtr = Number(parameters, "stopAtr");
        var bars = facts.History.Bars;
        if (bars.Count < 60 || facts.Atr14 is not { } atr || facts.Trend is null)
        {
            return RuleResult.None("insufficient_history", "Needs 60 bars, ATR(14) and a trend.");
        }

        var closes = bars.Select(b => b.Close).ToList();
        var last = closes.Count - 1;
        var ema = math.Ema(closes, emaPeriod);
        var (line, signal, histogram) = math.Macd(closes);
        if (ema[last] is not { } emaNow || ema[last - SlopeBars] is not { } emaThen || line[last] is not { } macd || signal[last] is not { } macdSignal
            || histogram[last] is not { } histNow || histogram[last - 1] is not { } histThen)
        {
            return RuleResult.None("insufficient_history", "EMA and MACD need more history.");
        }

        var close = closes[last];
        if (close <= emaNow)
        {
            return RuleResult.None("below_ema", $"Close {close} is not above the {emaPeriod}-day EMA {emaNow:F2}.");
        }

        if (emaNow <= emaThen)
        {
            return RuleResult.None("ema_not_rising", $"The {emaPeriod}-day EMA is not rising.");
        }

        if (macd <= macdSignal || histNow <= histThen)
        {
            return RuleResult.None("no_macd_confirmation", "MACD is not above its signal with a rising histogram.");
        }

        if (facts.RelStrengthRank is not { } rank || rank < minRelStrength)
        {
            return RuleResult.None("weak_relative_strength", $"Relative strength {facts.RelStrengthRank?.ToString(CultureInfo.InvariantCulture) ?? "unknown"} is below {minRelStrength}.");
        }

        if (facts.Trend != "uptrend")
        {
            return RuleResult.None("not_uptrend", "Momentum is only taken in an uptrend.");
        }

        var high52 = bars.Skip(Math.Max(0, bars.Count - 252)).Max(b => b.High);
        var fromHigh = (high52 - close) / high52;
        if (fromHigh > MaxFromHigh)
        {
            return RuleResult.None("far_from_high", $"Close is {fromHigh:P1} below the 52-week high; needs {MaxFromHigh:P0} or less.");
        }

        var entry = BuyStopAbove(bars[^1].High, 0.002m);
        var stop = Cent(entry - stopAtr * atr);
        var target = Cent(entry + 3m * (entry - stop));
        var rewardRisk = (target - entry) / (entry - stop);
        var slope = emaNow / emaThen - 1m;
        var setup = 0.5m * (1m - fromHigh / MaxFromHigh) + 0.5m * Math.Min(slope / 0.03m, 1m);
        var (score, breakdown) = Score(setup, rank, (facts.RelVolume20 ?? 1m) / 3m, facts.Trend, rewardRisk);
        var reason = string.Create(CultureInfo.InvariantCulture, $"Above a rising {emaPeriod}-day EMA with MACD turning up; relative strength {rank}.");
        return RuleResult.Signal(new SignalCandidate(RuleCode, facts.History.Symbol, "momentum", "buy_stop", entry, stop, target, Math.Round(rewardRisk, 2), score, breakdown, reason, bars[^1].SessionDate));
    }
}
