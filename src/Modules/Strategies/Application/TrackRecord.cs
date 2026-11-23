using System.Text.Json.Nodes;
using Astryx.Contracts.Discovery;
using Astryx.Contracts.MarketData;
using Astryx.Modules.Strategies.Application.Backtesting;
using Astryx.Modules.Strategies.Application.Rules;

namespace Astryx.Modules.Strategies.Application;

/// <summary>What became of one emitted signal. <paramref name="R"/> is the gain or loss in units of the planned risk.</summary>
/// <param name="Outcome"><c>pending</c> (no session yet), <c>not_triggered</c>, <c>open</c> (marked at the last close) or the exit reason.</param>
public sealed record SignalOutcome(string Symbol, DateOnly Session, string Outcome, DateOnly? EntryDate, DateOnly? ExitDate, decimal? R)
{
    public bool Closed => ExitDate is not null && Outcome != SignalFollower.Open;
}

/// <summary>A strategy's live, out-of-sample record: every signal it really emitted, followed forward (T-166).</summary>
/// <param name="MaxDrawdownR">The deepest fall of cumulative R (closed signals by exit date) from its high, as a positive number.</param>
public sealed record TrackRecordStats(
    DateOnly? Since, DateOnly? Through, int Signals, int Triggered, int Closed, int Open, decimal? WinRate, decimal? AverageR, decimal TotalR, decimal MaxDrawdownR);

/// <summary>Whether a strategy has run live long enough, and within its tested range, to be offered to beginners.</summary>
public sealed record Soak(bool Met, string Note);

/// <summary>
/// Follows a real signal forward on the bars that came after it, with the backtester's fill rules (T-160): the order is
/// worked on the next session as a day order, a buy stop fills at <c>max(open, entry)</c>, a stop at
/// <c>min(open, stop)</c>, a target at <c>max(open, target)</c>, the stop first when one bar touches both, and the
/// rule's own exit sells at the next open. Results are in R so they don't depend on anyone's account size; slippage
/// is left out (1 bp, well below the rounding of R).
/// </summary>
public static class SignalFollower
{
    public const string Pending = "pending";
    public const string NotTriggered = "not_triggered";
    public const string Open = "open";

    /// <summary>Minimum live record before a strategy is shown to beginners.</summary>
    public const int SoakClosedSignals = 20;

    public const int SoakDays = 60;

    /// <param name="bars">The symbol's daily bars, oldest first; only final bars are used.</param>
    public static SignalOutcome Follow(EmittedSignal signal, IReadOnlyList<DailyBar> bars, IStrategyExit? exit, JsonObject parameters)
    {
        var final = bars.Where(b => b.Final).ToList();
        var start = final.FindIndex(b => b.SessionDate > signal.Session);
        if (start < 0)
        {
            return new SignalOutcome(signal.Symbol, signal.Session, Pending, null, null, null);
        }

        var bar = final[start];
        decimal? price = signal.OrderType switch
        {
            "buy_stop" => bar.High >= signal.Entry ? Math.Max(bar.Open, signal.Entry) : null,
            "limit" => bar.Low <= signal.Entry ? Math.Min(bar.Open, signal.Entry) : null,
            _ => bar.Open,
        };
        if (price is not { } fill || fill <= signal.Stop)
        {
            return new SignalOutcome(signal.Symbol, signal.Session, NotTriggered, null, null, null);
        }

        var risk = fill - signal.Stop;
        SignalOutcome Exit(DateOnly date, decimal at, string reason) => new(signal.Symbol, signal.Session, reason, bar.SessionDate, date, Math.Round((at - fill) / risk, 4));

        if (bar.Low <= signal.Stop)
        {
            return Exit(bar.SessionDate, Math.Min(bar.Open, signal.Stop), "stop");
        }

        string? exitAtOpen = null;
        var held = 0;
        for (var i = start; i < final.Count; i++)
        {
            var today = final[i];
            if (i > start)
            {
                if (exitAtOpen is not null)
                {
                    return Exit(today.SessionDate, today.Open, exitAtOpen);
                }

                if (today.Low <= signal.Stop)
                {
                    return Exit(today.SessionDate, Math.Min(today.Open, signal.Stop), today.Open < signal.Stop ? "stop_gap" : "stop");
                }

                if (today.High >= signal.Target)
                {
                    return Exit(today.SessionDate, Math.Max(today.Open, signal.Target), "target");
                }

                held++;
                if (exit is not null)
                {
                    var from = Math.Max(0, i - (Backtester.ExitWindow - 1));
                    exitAtOpen = exit.ExitReason(new QuantFactsView(final.Skip(from).Take(i - from + 1).Select(b => b.Close).ToList()), held, parameters);
                }
            }
        }

        var last = final[^1];
        return new SignalOutcome(signal.Symbol, signal.Session, Open, bar.SessionDate, last.SessionDate, Math.Round((last.Close - fill) / risk, 4));
    }

    public static TrackRecordStats Aggregate(IReadOnlyList<SignalOutcome> outcomes)
    {
        var closed = outcomes.Where(o => o.Closed).OrderBy(o => o.ExitDate).ThenBy(o => o.Symbol, StringComparer.Ordinal).ToList();
        var open = outcomes.Count(o => o.Outcome == Open);
        var total = closed.Sum(o => o.R!.Value);
        return new TrackRecordStats(
            outcomes.Count == 0 ? null : outcomes.Min(o => o.Session),
            outcomes.Where(o => o.ExitDate is not null).Select(o => o.ExitDate).Max(),
            outcomes.Count,
            closed.Count + open,
            closed.Count,
            open,
            closed.Count == 0 ? null : Math.Round(closed.Count(o => o.R > 0m) / (decimal)closed.Count, 4),
            closed.Count == 0 ? null : Math.Round(total / closed.Count, 4),
            Math.Round(total, 4),
            MaxDrawdownR(closed.Select(o => o.R!.Value)));
    }

    /// <summary>The deepest fall of a cumulative R curve from its running high (positive).</summary>
    public static decimal MaxDrawdownR(IEnumerable<decimal> rs)
    {
        decimal cumulative = 0m, high = 0m, deepest = 0m;
        foreach (var r in rs)
        {
            cumulative += r;
            high = Math.Max(high, cumulative);
            deepest = Math.Max(deepest, high - cumulative);
        }

        return Math.Round(deepest, 4);
    }

    /// <summary>
    /// The soak gate: at least <see cref="SoakClosedSignals"/> closed signals over <see cref="SoakDays"/> days, and the
    /// live drawdown no deeper than the backtest's (both in R). Without a real backtest there is nothing to compare with.
    /// </summary>
    public static Soak SoakFor(TrackRecordStats live, decimal? backtestMaxDrawdownR, DateOnly today)
    {
        if (backtestMaxDrawdownR is null)
        {
            return new Soak(false, "No real backtest to compare the live record with yet.");
        }

        var days = live.Since is { } since ? today.DayNumber - since.DayNumber : 0;
        if (live.Closed < SoakClosedSignals || days < SoakDays)
        {
            return new Soak(false, $"Still in its live trial: {live.Closed} of {SoakClosedSignals} closed signals, {Math.Max(0, days)} of {SoakDays} days.");
        }

        return live.MaxDrawdownR <= backtestMaxDrawdownR
            ? new Soak(true, "Its live record is long enough and its losses stayed within its backtest.")
            : new Soak(false, $"Its live drawdown ({live.MaxDrawdownR:0.0}R) is deeper than its backtest's ({backtestMaxDrawdownR:0.0}R).");
    }
}
