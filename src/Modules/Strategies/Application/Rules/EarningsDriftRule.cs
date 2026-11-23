using System.Globalization;
using System.Text.Json.Nodes;
using Astryx.Contracts.Quant;
using static Astryx.Modules.Strategies.Application.Rules.RuleHelpers;

namespace Astryx.Modules.Strategies.Application.Rules;

/// <summary>
/// Earnings Drift (<c>earnings_drift@1</c>, rules-draft §4, approved 2026-11-23): a gap up of at least
/// <c>minGapPct</c> % on at least <c>volumeMultiple</c> × average volume within the last 3 sessions, holding since (no
/// close below the gap day's low, today within 2 % of the gap close) with relative strength of at least 60. Buy stop
/// above the high since the gap, stop just under the gap day's low, target 2.5R; exits after <c>holdDays</c> sessions.
/// The rule sees a gap; that it was earnings is checked against news when a live signal is published
/// (<see cref="StrategySignals"/>), which history can't do, so backtests take every qualifying gap.
/// </summary>
public sealed class EarningsDriftRule : IStrategyRule, IStrategyExit
{
    public const string RuleCode = "earnings_drift";
    public const int Lookback = 3;
    public const int MinRelStrength = 60;

    public string Code => RuleCode;

    public string CodeVersion => $"{RuleCode}@1";

    public RuleResult Evaluate(QuantFacts facts, JsonObject parameters)
    {
        var minGap = Number(parameters, "minGapPct") / 100m;
        var volumeMultiple = Number(parameters, "volumeMultiple");
        var bars = facts.History.Bars;
        if (bars.Count < 30 || facts.Atr14 is not { } atr)
        {
            return RuleResult.None("insufficient_history", "Needs 30 bars and ATR(14).");
        }

        var last = bars.Count - 1;
        int? gapDay = null;
        decimal gap = 0m, gapVolume = 0m;
        for (var g = last; g >= last - (Lookback - 1); g--)
        {
            var average = bars.Skip(g - 20).Take(20).Average(b => (decimal)b.Volume);
            var gapPct = bars[g].Open / bars[g - 1].Close - 1m;
            if (gapPct >= minGap && average > 0m && bars[g].Volume / average >= volumeMultiple)
            {
                (gapDay, gap, gapVolume) = (g, gapPct, bars[g].Volume / average);
                break;
            }
        }

        if (gapDay is not { } day)
        {
            return RuleResult.None("no_gap", $"No gap of {minGap:P0} on {volumeMultiple}× volume in the last {Lookback} sessions.");
        }

        var gapBar = bars[day];
        if (bars.Skip(day).Any(b => b.Close < gapBar.Low))
        {
            return RuleResult.None("gap_filled", "A close below the gap day's low: the gap failed.");
        }

        if (bars[last].Close < gapBar.Close * 0.98m)
        {
            return RuleResult.None("faded", "Today closed more than 2 % below the gap day's close.");
        }

        if (facts.RelStrengthRank is not { } rank || rank < MinRelStrength)
        {
            return RuleResult.None("weak_relative_strength", $"Relative strength {facts.RelStrengthRank?.ToString(CultureInfo.InvariantCulture) ?? "unknown"} is below {MinRelStrength}.");
        }

        var entry = BuyStopAbove(bars.Skip(day).Max(b => b.High), 0.002m);
        var stop = Cent(gapBar.Low - 0.25m * atr);
        var target = Cent(entry + 2.5m * (entry - stop));
        var rewardRisk = (target - entry) / (entry - stop);
        var setup = 0.5m * Math.Min(gap / 0.10m, 1m) + 0.5m * Math.Min(gapVolume / 4m, 1m);
        var (score, breakdown) = Score(setup, rank, gapVolume / 3m, facts.Trend, rewardRisk);
        var ago = last - day;
        var reason = string.Create(CultureInfo.InvariantCulture, $"Gapped up {gap * 100m:F1}% on {gapVolume:F1}× volume {(ago == 0 ? "today" : ago == 1 ? "yesterday" : $"{ago} sessions ago")} and is holding the gap.");
        return RuleResult.Signal(new SignalCandidate(RuleCode, facts.History.Symbol, "earnings", "buy_stop", entry, stop, target, Math.Round(rewardRisk, 2), score, breakdown, reason, bars[^1].SessionDate));
    }

    public string? ExitReason(QuantFactsView facts, int sessionsHeld, JsonObject parameters) =>
        sessionsHeld >= (int)Number(parameters, "holdDays") ? "time_exit" : null;
}
