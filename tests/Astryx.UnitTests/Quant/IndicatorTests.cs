using System.Globalization;
using Astryx.Modules.Quant.Application.Indicators;
using Astryx.Modules.Quant.Domain;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Astryx.UnitTests.Quant;

/// <summary>Indicator library (SYSTEM_DESIGN.md §13.3, §13.6, TASKS.md T-060).</summary>
public class IndicatorTests
{
    private const decimal Tolerance = 0.000001m;
    private static readonly IndicatorCalculator Calc = new();

    // ── Golden files: tools/gen_golden.py, an independent exact-arithmetic reference ─────────────────────────────

    public static TheoryData<string, string> GoldenColumns()
    {
        var data = new TheoryData<string, string>();
        foreach (var symbol in new[] { "nvda", "tsla" })
        {
            foreach (var column in new[] { "sma20", "sma50", "ema20", "rsi14", "macd", "macd_signal", "macd_hist", "atr14", "relvol20", "roc20", "vol20" })
            {
                data.Add(symbol, column);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(GoldenColumns))]
    public void Matches_the_golden_file_at_1e_6(string symbol, string column)
    {
        var bars = Fixtures.Bars(symbol);
        var closes = bars.Select(b => b.Close).ToList();
        var expected = Fixtures.Expected(symbol, column);
        var macd = Calc.Macd(closes);
        var actual = column switch
        {
            "sma20" => Calc.Sma(closes, 20),
            "sma50" => Calc.Sma(closes, 50),
            "ema20" => Calc.Ema(closes, 20),
            "rsi14" => Calc.Rsi(closes),
            "macd" => macd.Macd,
            "macd_signal" => macd.Signal,
            "macd_hist" => macd.Histogram,
            "atr14" => Calc.Atr(bars),
            "relvol20" => Calc.RelativeVolume(bars),
            "roc20" => Calc.Momentum(closes),
            "vol20" => Calc.Volatility(closes),
            _ => throw new ArgumentOutOfRangeException(nameof(column)),
        };

        actual.Status.ShouldBe(IndicatorStatus.Ok);
        actual.Values.Count.ShouldBe(expected.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            if (expected[i] is null)
            {
                actual.Values[i].ShouldBeNull($"{column}[{i}] is warm-up");
            }
            else
            {
                actual.Values[i].ShouldNotBeNull($"{column}[{i}]");
                Math.Abs(actual.Values[i]!.Value - expected[i]!.Value).ShouldBeLessThanOrEqualTo(Tolerance, $"{column}[{i}]");
            }
        }
    }

    [Theory]
    [InlineData("nvda")]
    [InlineData("tsla")]
    public void Relative_strength_score_and_trend_match_the_golden_summary(string symbol)
    {
        var closes = Fixtures.Bars(symbol).Select(b => b.Close).ToList();
        var summary = Fixtures.Summary(symbol);

        Math.Abs(RelativeStrength.Score(closes)!.Value - decimal.Parse(summary["rs_score"], CultureInfo.InvariantCulture)).ShouldBeLessThanOrEqualTo(Tolerance);
        var trend = TrendClassifier.Classify(closes, Calc);
        trend.State.ToString().ToLowerInvariant().ShouldBe(summary["trend"]);
        trend.ShortHistory.ShouldBe(summary["trend_short_history"] == "true");
    }

    // ── Hand-computed micro cases (§13.6) ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Sma_of_one_to_five()
    {
        Calc.Sma([1m, 2m, 3m, 4m, 5m], 3).Values.ShouldBe([null, null, 2m, 3m, 4m]);
    }

    [Fact]
    public void Ema_is_seeded_with_the_sma_then_smoothed()
    {
        // n = 3, k = 0.5: seed (1+2+3)/3 = 2; then 4·0.5 + 2·0.5 = 3; then 5·0.5 + 3·0.5 = 4.
        var ema = Calc.Ema([1m, 2m, 3m, 4m, 5m], 3);
        ema.Values.ShouldBe([null, null, 2m, 3m, 4m]);
        ema.ReliableFrom.ShouldBe(4); // 2n − 1, clamped to the series
    }

    [Fact]
    public void Rsi_is_100_on_all_gains_and_50_on_a_flat_series()
    {
        var rising = Enumerable.Range(1, 30).Select(i => (decimal)i).ToList();
        var flat = Enumerable.Repeat(42m, 30).ToList();

        Calc.Rsi(rising).Values.Skip(14).ShouldAllBe(v => v == 100m);
        Calc.Rsi(flat).Values.Skip(14).ShouldAllBe(v => v == 50m);
        Calc.Rsi(rising).Values.Take(14).ShouldAllBe(v => v == null);
    }

    [Fact]
    public void Atr_is_zero_on_constant_bars_and_uses_gaps_through_the_previous_close()
    {
        var constant = Enumerable.Range(0, 20).Select(i => Bar(10m, 10m, 10m, 10m)).ToList();
        Calc.Atr(constant).Values.Skip(14).ShouldAllBe(v => v == 0m);

        // A gap up: TR = |H − C_prev| = 12 − 10 = 2 even though H − L = 0.5.
        var gap = new List<Bar> { Bar(10m, 10m, 10m, 10m), Bar(11.5m, 12m, 11.5m, 12m) };
        Calc.Atr(gap, period: 1).Values[1].ShouldBe(2m);
    }

    [Fact]
    public void Relative_volume_compares_against_the_previous_bars_only()
    {
        var bars = Enumerable.Range(0, 3).Select(_ => Bar(1m, 1m, 1m, 1m, volume: 100)).Append(Bar(1m, 1m, 1m, 1m, volume: 250)).ToList();

        Calc.RelativeVolume(bars, period: 3).Values.ShouldBe([null, null, null, 2.5m]);
    }

    [Fact]
    public void Momentum_is_the_rate_of_change()
    {
        Calc.Momentum([100m, 110m, 121m], period: 2).Values.ShouldBe([null, null, 0.21m]);
    }

    [Fact]
    public void Volatility_of_a_constant_growth_series_is_zero_and_rounded_to_6_dp()
    {
        var closes = Enumerable.Range(0, 25).Select(i => 100m * (decimal)Math.Pow(1.01, i)).ToList(); // float-ok: test data only

        var values = Calc.Volatility(closes).Values.Skip(20).ToList();
        values.ShouldAllBe(v => v != null && Math.Abs(v.Value) <= 0.000001m);
        values.ShouldAllBe(v => decimal.Round(v!.Value, 6) == v.Value);
    }

    [Fact]
    public void Macd_signal_and_histogram_start_at_bar_34()
    {
        var closes = Enumerable.Range(1, 40).Select(i => 100m + i).ToList();

        var macd = Calc.Macd(closes);
        macd.Macd.Values.ToList().FindIndex(v => v != null).ShouldBe(25);
        macd.Signal.Values.ToList().FindIndex(v => v != null).ShouldBe(33);
        macd.Histogram.Values.ToList().FindIndex(v => v != null).ShouldBe(33);
    }

    // ── Insufficient history: never partial values (§13.5) ─────────────────────────────────────────────────────────

    public static TheoryData<string, int> MinimumBars() => new()
    {
        { "sma20", 20 }, { "ema20", 20 }, { "rsi14", 15 }, { "macd", 34 }, { "atr14", 15 }, { "relvol20", 21 }, { "roc20", 21 }, { "vol20", 21 },
    };

    [Theory]
    [MemberData(nameof(MinimumBars))]
    public void One_bar_short_of_the_minimum_is_insufficient_with_no_values(string indicator, int minBars)
    {
        IndicatorSeries Compute(List<Bar> bars)
        {
            var closes = bars.Select(b => b.Close).ToList();
            return indicator switch
            {
                "sma20" => Calc.Sma(closes, 20),
                "ema20" => Calc.Ema(closes, 20),
                "rsi14" => Calc.Rsi(closes),
                "macd" => Calc.Macd(closes).Histogram,
                "atr14" => Calc.Atr(bars),
                "relvol20" => Calc.RelativeVolume(bars),
                "roc20" => Calc.Momentum(closes),
                "vol20" => Calc.Volatility(closes),
                _ => throw new ArgumentOutOfRangeException(nameof(indicator)),
            };
        }

        var all = Fixtures.Bars("nvda");
        var short1 = Compute(all.Take(minBars - 1).ToList());
        var exact = Compute(all.Take(minBars).ToList());

        short1.Status.ShouldBe(IndicatorStatus.Insufficient);
        short1.Values.ShouldAllBe(v => v == null);
        short1.MinBars.ShouldBe(minBars);
        exact.Status.ShouldBe(IndicatorStatus.Ok);
        exact.Last.ShouldNotBeNull();
    }

    [Fact]
    public void Relative_strength_and_trend_need_their_history()
    {
        var closes = Fixtures.Bars("nvda").Select(b => b.Close).ToList();

        RelativeStrength.Score(closes.Take(252).ToList()).ShouldBeNull();
        RelativeStrength.Score(closes.Take(253).ToList()).ShouldNotBeNull();
        TrendClassifier.Classify(closes.TakeLast(120).ToList(), Calc).ShortHistory.ShouldBeTrue();
        TrendClassifier.Classify(closes.TakeLast(49).ToList(), Calc).State.ShouldBe(TrendState.Insufficient);
    }

    [Fact]
    public void Relative_strength_ranks_1_to_99_with_shared_ties()
    {
        var ranks = RelativeStrength.Rank(new Dictionary<string, decimal> { ["A"] = -0.2m, ["B"] = 0.1m, ["C"] = 0.1m, ["D"] = 0.5m, ["E"] = 0.9m });

        ranks["A"].ShouldBe(1);
        ranks["B"].ShouldBe(ranks["C"]);
        ranks["B"].ShouldBe(26); // 1 + 98 × 1/4 = 25.5 → 26 (half-even)
        ranks["D"].ShouldBe(74);
        ranks["E"].ShouldBe(99);
        RelativeStrength.Rank(new Dictionary<string, decimal> { ["ONLY"] = 0.3m })["ONLY"].ShouldBe(50);
    }

    // ── Property tests (§13.6) ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>60–150 bars of cent prices from $1 to $1,000 with consistent OHLC.</summary>
    private static Arbitrary<List<Bar>> BarSeries() =>
        Arb.From(
            from count in Gen.Choose(60, 150)
            from rows in Gen.ListOf(
                from close in Gen.Choose(100, 100_000)
                from up in Gen.Choose(0, 500)
                from down in Gen.Choose(0, 500)
                from open in Gen.Choose(100, 100_000)
                from volume in Gen.Choose(1, 1_000_000)
                select (close, up, down, open, volume),
                count)
            select rows.Select(r =>
            {
                var close = r.close / 100m;
                var open = r.open / 100m;
                return new Bar(DateOnly.MinValue, open, Math.Max(open, close) + r.up / 100m, Math.Max(0.01m, Math.Min(open, close) - r.down / 100m), close, r.volume);
            }).ToList());

    [Property(MaxTest = 200)]
    public Property Sma_lies_within_its_window() =>
        Prop.ForAll(BarSeries(), bars =>
        {
            var closes = bars.Select(b => b.Close).ToList();
            var sma = Calc.Sma(closes, 10).Values;
            return Enumerable.Range(9, closes.Count - 9).All(i =>
            {
                var window = closes.Skip(i - 9).Take(10).ToList();
                return sma[i] >= window.Min() && sma[i] <= window.Max();
            });
        });

    [Property(MaxTest = 200)]
    public Property Rsi_is_between_0_and_100_and_atr_is_non_negative() =>
        Prop.ForAll(BarSeries(), bars =>
        {
            var closes = bars.Select(b => b.Close).ToList();
            return Calc.Rsi(closes).Values.All(v => v is null or >= 0m and <= 100m)
                && Calc.Atr(bars).Values.All(v => v is null or >= 0m);
        });

    [Property(MaxTest = 200)]
    public Property Shifting_prices_leaves_rsi_atr_and_macd_histogram_unchanged() =>
        Prop.ForAll(BarSeries(), bars =>
        {
            var shifted = bars.Select(b => b with { Open = b.Open + 50m, High = b.High + 50m, Low = b.Low + 50m, Close = b.Close + 50m }).ToList();
            var closes = bars.Select(b => b.Close).ToList();
            var shiftedCloses = shifted.Select(b => b.Close).ToList();
            return Close(Calc.Rsi(closes), Calc.Rsi(shiftedCloses), 0.000000001m)
                && Close(Calc.Atr(bars), Calc.Atr(shifted), 0.000000001m)
                && Close(Calc.Macd(closes).Histogram, Calc.Macd(shiftedCloses).Histogram, 0.000000001m);
        });

    [Property(MaxTest = 200)]
    public Property Scaling_prices_by_k_scales_atr_by_k() =>
        Prop.ForAll(BarSeries(), Arb.From(Gen.Choose(2, 9)), (bars, k) =>
        {
            var scaled = bars.Select(b => b with { Open = b.Open * k, High = b.High * k, Low = b.Low * k, Close = b.Close * k }).ToList();
            var atr = Calc.Atr(bars).Values;
            var scaledAtr = Calc.Atr(scaled).Values;
            return atr.Zip(scaledAtr).All(p => p.First is null ? p.Second is null : Math.Abs(p.First.Value * k - p.Second!.Value) <= 0.000000001m);
        });

    [Fact]
    public void Results_are_deterministic()
    {
        var bars = Fixtures.Bars("nvda");
        var closes = bars.Select(b => b.Close).ToList();

        Calc.Macd(closes).Histogram.Values.ShouldBe(Calc.Macd(closes.ToList()).Histogram.Values);
        Calc.Volatility(closes).Values.ShouldBe(Calc.Volatility(closes.ToList()).Values);
    }

    private static bool Close(IndicatorSeries a, IndicatorSeries b, decimal tolerance) =>
        a.Values.Zip(b.Values).All(p => p.First is null ? p.Second is null : p.Second is not null && Math.Abs(p.First.Value - p.Second.Value) <= tolerance);

    private static Bar Bar(decimal open, decimal high, decimal low, decimal close, long volume = 1000) => new(DateOnly.MinValue, open, high, low, close, volume);

    /// <summary>Reads tests/fixtures/quant (the bars and the golden output of tools/gen_golden.py).</summary>
    private static class Fixtures
    {
        private static readonly string Directory = Path.Combine(RepositoryRoot(), "tests", "fixtures", "quant");

        public static List<Bar> Bars(string symbol) =>
            Rows($"{symbol}_daily.csv").Select(r => new Bar(
                DateOnly.Parse(r["session_date"], CultureInfo.InvariantCulture),
                Dec(r["open"]), Dec(r["high"]), Dec(r["low"]), Dec(r["close"]),
                long.Parse(r["volume"], CultureInfo.InvariantCulture))).ToList();

        public static List<decimal?> Expected(string symbol, string column) =>
            Rows($"{symbol}_daily.expected.csv").Select(r => r[column].Length == 0 ? (decimal?)null : Dec(r[column])).ToList();

        public static Dictionary<string, string> Summary(string symbol) =>
            Rows($"{symbol}_daily.summary.csv").ToDictionary(r => r["metric"], r => r["value"]);

        private static decimal Dec(string value) => decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);

        private static IEnumerable<Dictionary<string, string>> Rows(string file)
        {
            var lines = File.ReadAllLines(Path.Combine(Directory, file));
            var header = lines[0].Split(',');
            return lines.Skip(1).Where(l => l.Length > 0).Select(l =>
            {
                var cells = l.Split(',');
                return header.Select((h, i) => (h, cells[i])).ToDictionary(x => x.h, x => x.Item2);
            });
        }

        private static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Astryx.slnx")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new InvalidOperationException("Astryx.slnx not found above the test output directory.");
        }
    }
}
