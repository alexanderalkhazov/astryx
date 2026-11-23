using System.Globalization;
using System.Text.Json.Nodes;

namespace Astryx.Modules.Strategies.Application.Backtesting;

/// <summary>
/// Run statistics in the shape the Strategy lab reads (<c>backtest_runs.metrics</c>, §13): full period, in-sample and
/// out-of-sample, as decimal strings. Returns are fractions (0.169 = 16.9 %); Sharpe uses daily returns, no risk-free
/// rate, annualised by √252.
/// </summary>
public static class BacktestMetrics
{
    public const int SessionsPerYear = 252;

    public static JsonObject Compute(BacktestResult result, decimal startingCash)
    {
        var full = Segment(result.Equity, result.Trades, startingCash);
        var trades = result.Trades;
        var wins = trades.Where(t => t.Pnl > 0m).Sum(t => t.Pnl);
        var losses = -trades.Where(t => t.Pnl < 0m).Sum(t => t.Pnl);
        full["trades"] = trades.Count;
        full["multiple"] = Text(result.Equity.Count == 0 ? 1m : result.Equity[^1].Equity / startingCash, 2);
        full["avgTradePct"] = Text(trades.Count == 0 ? 0m : trades.Average(t => t.ExitPrice / t.EntryPrice - 1m), 4);
        full["expectancyR"] = Text(trades.Count == 0 ? 0m : trades.Average(t => t.RMultiple), 2);
        full["profitFactor"] = losses == 0m ? null : Text(wins / losses, 2);

        var inSample = result.Equity.Where(e => e.Date < result.OutOfSampleStart).ToList();
        var outOfSample = result.Equity.Where(e => e.Date >= result.OutOfSampleStart).ToList();
        return new JsonObject
        {
            ["full"] = full,
            ["inSample"] = Segment(inSample, trades.Where(t => t.EntryDate < result.OutOfSampleStart).ToList(), inSample.FirstOrDefault().Equity),
            ["outOfSample"] = Segment(outOfSample, trades.Where(t => t.EntryDate >= result.OutOfSampleStart).ToList(), outOfSample.FirstOrDefault().Equity),
        };
    }

    public static JsonArray EquityCurve(BacktestResult result) =>
        new(result.Equity.Select(e => (JsonNode)new JsonArray(Date(e.Date), Text(e.Equity, 2))).ToArray());

    public static JsonArray DrawdownCurve(BacktestResult result)
    {
        var peak = 0m;
        return new JsonArray(result.Equity.Select(e =>
        {
            peak = Math.Max(peak, e.Equity);
            return (JsonNode)new JsonArray(Date(e.Date), Text(peak == 0m ? 0m : e.Equity / peak - 1m, 4));
        }).ToArray());
    }

    /// <summary>Each calendar month's return from the last equity of the previous month (or the start) to its own last.</summary>
    public static JsonArray MonthlyReturns(BacktestResult result, decimal startingCash)
    {
        var months = new JsonArray();
        var previous = startingCash;
        foreach (var month in result.Equity.GroupBy(e => (e.Date.Year, e.Date.Month)))
        {
            var last = month.Last().Equity;
            months.Add(new JsonArray($"{month.Key.Year:D4}-{month.Key.Month:D2}", Text(previous == 0m ? 0m : last / previous - 1m, 4)));
            previous = last;
        }

        return months;
    }

    private static JsonObject Segment(IReadOnlyList<(DateOnly Date, decimal Equity)> equity, IReadOnlyList<SimulatedTrade> trades, decimal start)
    {
        if (equity.Count < 2 || start <= 0m)
        {
            return new JsonObject { ["cagr"] = "0.000", ["sharpe"] = "0.00", ["winRate"] = Text(WinRate(trades), 2), ["maxDrawdown"] = "0.000" };
        }

        var years = (decimal)equity.Count / SessionsPerYear;
        var growth = equity[^1].Equity / start;
        var cagr = growth > 0m ? DecimalMath.Pow(growth, 1m / years) - 1m : -1m;

        var returns = new List<decimal>(equity.Count);
        for (var i = 1; i < equity.Count; i++)
        {
            returns.Add(equity[i - 1].Equity == 0m ? 0m : equity[i].Equity / equity[i - 1].Equity - 1m);
        }

        var mean = returns.Average();
        var variance = returns.Sum(r => (r - mean) * (r - mean)) / Math.Max(1, returns.Count - 1);
        var deviation = DecimalMath.Sqrt(variance);
        var sharpe = deviation == 0m ? 0m : mean / deviation * DecimalMath.Sqrt(SessionsPerYear);

        var peak = 0m;
        var worst = 0m;
        foreach (var (_, value) in equity)
        {
            peak = Math.Max(peak, value);
            worst = Math.Min(worst, value / peak - 1m);
        }

        return new JsonObject { ["cagr"] = Text(cagr, 3), ["sharpe"] = Text(sharpe, 2), ["winRate"] = Text(WinRate(trades), 2), ["maxDrawdown"] = Text(worst, 3) };
    }

    private static decimal WinRate(IReadOnlyList<SimulatedTrade> trades) => trades.Count == 0 ? 0m : (decimal)trades.Count(t => t.Pnl > 0m) / trades.Count;

    private static string Text(decimal value, int decimals) =>
        Math.Round(value, decimals, MidpointRounding.ToEven).ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
