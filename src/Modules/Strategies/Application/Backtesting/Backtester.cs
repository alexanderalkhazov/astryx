using System.Text.Json.Nodes;
using Astryx.Contracts.MarketData;
using Astryx.Contracts.Quant;
using Astryx.Modules.Strategies.Application.Rules;

namespace Astryx.Modules.Strategies.Application.Backtesting;

/// <summary>How a backtest trades: the live defaults (SYSTEM_DESIGN.md §14.4, §15) unless a run says otherwise.</summary>
public sealed record BacktestSettings(
    decimal StartingCash = 100_000m,
    decimal RiskPerTradePct = 0.0075m,
    decimal MaxPositionWeightPct = 0.12m,
    int MaxOpenPositions = 12,
    int SlippageBps = 1,
    decimal MinSlippage = 0.01m,
    decimal FeePerOrder = 0m,
    decimal OutOfSampleFraction = 0.3m);

/// <summary>One completed (or, at the end of the data, marked-to-market) trade.</summary>
public sealed record SimulatedTrade(
    Guid InstrumentId, string Symbol, DateOnly EntryDate, DateOnly ExitDate, decimal EntryPrice, decimal ExitPrice, decimal Quantity, decimal Stop, decimal Pnl, decimal RMultiple, string ExitReason);

/// <summary>A run's output: trades and the end-of-session equity curve from the first tradable session.</summary>
public sealed record BacktestResult(IReadOnlyList<SimulatedTrade> Trades, IReadOnlyList<(DateOnly Date, decimal Equity)> Equity, DateOnly OutOfSampleStart);

/// <summary>
/// Replays a strategy rule over history (TASKS.md T-160) with the live rule code, sizing and costs, and no look-ahead:
/// <list type="bullet">
/// <item>a signal is evaluated on a session's <b>completed</b> bar with indicator values through that bar, and its order
/// is worked on the <b>next</b> session (a day order: unfilled at that close, it lapses);</item>
/// <item>a buy stop fills at <c>max(open, entry)</c>, a limit at <c>min(open, entry)</c>, a market order at the open, plus
/// slippage;</item>
/// <item>exits use each later session's range: a stop at <c>min(open, stop)</c> (a gap fills at the open), a target at
/// <c>max(open, target)</c>; when one bar touches both, the stop is assumed first. A position opened that session can
/// also stop out that session;</item>
/// <item>size is the smallest of the risk, weight-cap and cash bounds; at most <c>MaxOpenPositions</c> positions and
/// pending entries, highest scores first.</item>
/// </list>
/// Live signals can trigger the same day; entering from the next session instead is the conservative difference.
/// </summary>
public static class Backtester
{
    /// <summary>Bars handed to the rule as its history window, like the live facts' lookback.</summary>
    public const int HistoryWindow = 300;

    private sealed record Position(int Series, DateOnly EntryDate, decimal Entry, decimal Stop, decimal Target, decimal Quantity);

    private sealed record Pending(int Series, SignalCandidate Candidate, decimal Quantity);

    public static BacktestResult Run(BacktestDataset data, IStrategyRule rule, JsonObject parameters, BacktestSettings settings)
    {
        var index = data.Series.Select(s => s.Bars.Select((b, i) => (b.SessionDate, i)).ToDictionary(x => x.SessionDate, x => x.i)).ToList();
        var lastClose = new decimal?[data.Series.Count];
        var positions = new List<Position>();
        var pending = new List<Pending>();
        var trades = new List<SimulatedTrade>();
        var equity = new List<(DateOnly, decimal)>();
        var cash = settings.StartingCash;

        // The curve starts on the first session any symbol has a universe-wide rank: before that no rule can run.
        var first = data.Series.SelectMany(x => x.Bars.Where((_, i) => x.RelStrengthRank[i] is not null).Select(b => b.SessionDate).Take(1)).DefaultIfEmpty(DateOnly.MaxValue).Min();

        foreach (var date in data.Sessions)
        {
            // 1. Yesterday's signals: work each entry against today's bar, then let it stop out the same day.
            foreach (var order in pending)
            {
                var s = data.Series[order.Series];
                if (!index[order.Series].TryGetValue(date, out var i))
                {
                    continue; // no bar today: the day order lapses
                }

                var bar = s.Bars[i];
                var c = order.Candidate;
                decimal? price = c.OrderType switch
                {
                    "buy_stop" => bar.High >= c.Entry ? Math.Max(bar.Open, c.Entry) : null,
                    "limit" => bar.Low <= c.Entry ? Math.Min(bar.Open, c.Entry) : null,
                    _ => bar.Open,
                };
                if (price is not { } raw)
                {
                    continue;
                }

                var fill = raw + Slip(raw, settings);
                var quantity = Math.Min(order.Quantity, Math.Floor((cash - settings.FeePerOrder) / fill));
                if (quantity <= 0m || fill <= c.Stop)
                {
                    continue; // no cash left, or it opened through the stop: the plan no longer stands
                }

                cash -= quantity * fill + settings.FeePerOrder;
                var position = new Position(order.Series, date, fill, c.Stop, c.Target, quantity);
                if (bar.Low <= c.Stop)
                {
                    cash += Close(position, s, date, Math.Min(bar.Open, c.Stop), "stop", settings, trades);
                }
                else
                {
                    positions.Add(position);
                }
            }

            pending.Clear();

            // 2. Open positions against today's range.
            foreach (var position in positions.ToList())
            {
                var s = data.Series[position.Series];
                if (position.EntryDate == date || !index[position.Series].TryGetValue(date, out var i))
                {
                    continue;
                }

                var bar = s.Bars[i];
                if (bar.Low <= position.Stop)
                {
                    cash += Close(position, s, date, Math.Min(bar.Open, position.Stop), bar.Open < position.Stop ? "stop_gap" : "stop", settings, trades);
                    positions.Remove(position);
                }
                else if (bar.High >= position.Target)
                {
                    cash += Close(position, s, date, Math.Max(bar.Open, position.Target), "target", settings, trades);
                    positions.Remove(position);
                }
            }

            for (var s = 0; s < data.Series.Count; s++)
            {
                if (index[s].TryGetValue(date, out var i))
                {
                    lastClose[s] = data.Series[s].Bars[i].Close;
                }
            }

            var value = cash + positions.Sum(p => p.Quantity * (lastClose[p.Series] ?? p.Entry));

            // 3. Tonight's signals, for tomorrow.
            var held = positions.Select(p => p.Series).ToHashSet();
            var candidates = new List<(int Series, SignalCandidate Candidate)>();
            for (var s = 0; s < data.Series.Count; s++)
            {
                if (held.Contains(s) || !index[s].TryGetValue(date, out var i) || data.Series[s].RelStrengthRank[i] is null)
                {
                    continue; // before the universe-wide rank exists, live rules can't be evaluated either
                }

                if (rule.Evaluate(FactsAt(data.Series[s], i), parameters).Candidate is { } candidate)
                {
                    candidates.Add((s, candidate));
                }
            }

            var available = cash;
            foreach (var (s, candidate) in candidates.OrderByDescending(c => c.Candidate.Score).ThenBy(c => data.Series[c.Series].Symbol, StringComparer.Ordinal))
            {
                if (positions.Count + pending.Count >= settings.MaxOpenPositions)
                {
                    break;
                }

                var slip = Slip(candidate.Entry, settings);
                var riskPerShare = candidate.Entry - candidate.Stop + 2m * slip;
                if (riskPerShare <= 0m)
                {
                    continue;
                }

                var quantity = Math.Min(
                    Math.Min(Math.Floor(value * settings.RiskPerTradePct / riskPerShare), Math.Floor(value * settings.MaxPositionWeightPct / candidate.Entry)),
                    Math.Floor(available / (candidate.Entry + slip)));
                if (quantity < 1m)
                {
                    continue;
                }

                available -= quantity * (candidate.Entry + slip);
                pending.Add(new Pending(s, candidate, quantity));
            }

            if (date >= first)
            {
                equity.Add((date, value));
            }
        }

        // What is still open at the end is marked to market so the curve and the trade list agree.
        var end = equity.Count > 0 ? equity[^1].Item1 : data.Sessions.LastOrDefault();
        foreach (var position in positions)
        {
            var close = lastClose[position.Series] ?? position.Entry;
            Close(position, data.Series[position.Series], end, close, "end_of_data", settings with { SlippageBps = 0, MinSlippage = 0m, FeePerOrder = 0m }, trades);
        }

        var oos = equity.Count == 0 ? end : equity[Math.Min(equity.Count - 1, (int)Math.Floor(equity.Count * (1m - settings.OutOfSampleFraction)))].Item1;
        return new BacktestResult(trades, equity, oos);
    }

    /// <summary>The facts a live scan would have seen at the close of bar <paramref name="i"/>.</summary>
    public static QuantFacts FactsAt(BacktestSeries s, int i)
    {
        var from = Math.Max(0, i - HistoryWindow + 1);
        var bars = new DailyBar[i - from + 1];
        for (var k = from; k <= i; k++)
        {
            bars[k - from] = s.Bars[k];
        }

        var history = new DailyHistory(s.InstrumentId, s.Symbol, bars, null, Stale: false, []);
        return new QuantFacts(history, s.Atr14[i], s.RelVolume20[i], s.RelStrengthRank[i], s.Trend[i], s.Rsi14[i]);
    }

    /// <summary>Sells the position and records the trade; returns the cash it frees.</summary>
    private static decimal Close(Position position, BacktestSeries s, DateOnly date, decimal rawPrice, string reason, BacktestSettings settings, List<SimulatedTrade> trades)
    {
        var price = Math.Max(0.01m, rawPrice - Slip(rawPrice, settings));
        var pnl = position.Quantity * (price - position.Entry) - 2m * settings.FeePerOrder;
        var risk = position.Entry - position.Stop;
        trades.Add(new SimulatedTrade(
            s.InstrumentId, s.Symbol, position.EntryDate, date, position.Entry, price, position.Quantity, position.Stop, pnl,
            risk > 0m ? (price - position.Entry) / risk : 0m, reason));
        return position.Quantity * price - settings.FeePerOrder;
    }

    /// <summary>The live cost model: basis points of the price, rounded up to the cent, at least the minimum.</summary>
    private static decimal Slip(decimal price, BacktestSettings settings) =>
        Math.Max(settings.MinSlippage, Math.Ceiling(price * settings.SlippageBps / 10_000m * 100m) / 100m);
}
