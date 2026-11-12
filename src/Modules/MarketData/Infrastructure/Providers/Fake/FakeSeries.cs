using Astryx.Modules.MarketData.Application.Providers;

namespace Astryx.Modules.MarketData.Infrastructure.Providers.Fake;

/// <summary>One synthetic daily bar (prices already rounded to cents).</summary>
internal sealed record DailyBar(ProviderSession Session, decimal Open, decimal High, decimal Low, decimal Close, long Volume);

/// <summary>
/// Builds a symbol's daily history so it ends exactly at the fixture's previous close and current-session values.
/// History is generated <b>backwards</b> from that anchor, with the shape set by the row's <c>pattern</c> (the setups
/// the scanner should find in M3). The same seed, symbol and current session always give the same bars.
/// </summary>
internal static class FakeSeries
{
    private const decimal Cent = 0.01m;

    /// <summary>History (oldest first) followed by the current session's bar.</summary>
    public static IReadOnlyList<DailyBar> Daily(FixtureInstrument instrument, IReadOnlyList<ProviderSession> history, ProviderSession current, int seed)
    {
        var random = SeededRandom.For(seed, instrument.Symbol, current.SessionDate.DayNumber);
        var count = history.Count;
        var closes = new decimal[count];
        var volatility = BaseVolatility(instrument);
        closes[count - 1] = instrument.PrevClose;

        // Walk back: close[i] = close[i+1] / (1 + return of day i+1). "sessionsAgo" is days before the current session.
        for (var i = count - 2; i >= 0; i--)
        {
            var sessionsAgo = count - 1 - i;
            var (drift, vol) = Regime(instrument.Pattern, sessionsAgo, volatility);
            var dayReturn = drift + (vol * random.NextGaussian());
            if (instrument.Pattern == "earnings" && sessionsAgo == 10)
            {
                dayReturn = 0.09m; // the post-earnings gap the drift follows
            }

            var close = closes[i + 1] / (1 + dayReturn);
            if (instrument.Pattern == "breakout" && instrument.Level is { } level && sessionsAgo < 30)
            {
                close = Math.Clamp(close, level * 0.935m, level * 0.993m); // the base sits just under resistance
            }

            closes[i] = Math.Max(Round(close), 1m);
        }

        var bars = new List<DailyBar>(count + 1);
        for (var i = 0; i < count; i++)
        {
            var sessionsAgo = count - i; // closes[count-1] is yesterday, one session ago
            var previousClose = i == 0 ? closes[0] : closes[i - 1];
            var (_, vol) = Regime(instrument.Pattern, sessionsAgo, volatility);
            var open = Round(previousClose * (1 + (vol * 0.3m * random.NextGaussian())));
            var close = closes[i];
            var high = Round(Math.Max(open, close) * (1 + (vol * 0.4m * Math.Abs(random.NextGaussian()))));
            var low = Round(Math.Min(open, close) * (1 - (vol * 0.4m * Math.Abs(random.NextGaussian()))));
            if (instrument.Pattern == "breakout" && instrument.Level is { } level && sessionsAgo <= 30)
            {
                open = Math.Min(open, Round(level * 0.995m));
                high = Math.Min(high, level); // resistance holds until today
            }

            var volumeFactor = random.Between(0.55m, 1.45m) * VolumeRegime(instrument.Pattern, sessionsAgo);
            bars.Add(Normalize(history[i], open, high, low, close, (long)(instrument.AverageVolume * volumeFactor)));
        }

        bars.Add(CurrentBar(instrument, current));
        return bars;
    }

    /// <summary>The current session's bar from the fixture; missing open/high/low are derived from last and prev close.</summary>
    public static DailyBar CurrentBar(FixtureInstrument instrument, ProviderSession current)
    {
        var last = instrument.Last;
        var open = instrument.Open ?? Round(instrument.PrevClose + ((last - instrument.PrevClose) * 0.25m));
        var high = instrument.High ?? Round(Math.Max(open, last) * 1.004m);
        var low = instrument.Low ?? Round(Math.Min(open, last) * 0.996m);
        return Normalize(current, open, high, low, last, instrument.Volume);
    }

    /// <summary>
    /// Splits one session into intraday bars ending at <paramref name="until"/>: a seeded Brownian bridge from open to
    /// close that touches the day's high and low; volume is U-shaped across the session.
    /// </summary>
    public static IReadOnlyList<ProviderBar> Intraday(string symbol, DailyBar day, BarInterval interval, DateTimeOffset until, int seed)
    {
        var session = day.Session;
        var minutes = interval.Minutes();
        var total = (int)((session.CloseAt!.Value - session.OpenAt!.Value).TotalMinutes / minutes);
        var elapsed = Math.Min(total, (int)Math.Ceiling((until - session.OpenAt.Value).TotalMinutes / minutes));
        if (elapsed <= 0)
        {
            return [];
        }

        // A forming session's path ends at the current price after the bars elapsed so far.
        var steps = until < session.CloseAt ? elapsed : total;
        var random = SeededRandom.For(seed, symbol, session.SessionDate.DayNumber, minutes);
        var path = new decimal[steps + 1];
        for (var k = 1; k <= steps; k++)
        {
            path[k] = path[k - 1] + random.NextGaussian();
        }

        // Bridge: pin the walk to 0 at both ends, then lay it over the straight line from open to close.
        var span = Math.Max(day.High - day.Low, Cent);
        var maxDeviation = Math.Max(Enumerable.Range(0, steps + 1).Max(k => Math.Abs(path[k] - (path[steps] * k / steps))), 0.0001m);
        var prices = new decimal[steps + 1];
        for (var k = 0; k <= steps; k++)
        {
            var line = day.Open + ((day.Close - day.Open) * k / steps);
            var bridge = (path[k] - (path[steps] * k / steps)) / maxDeviation * span * 0.5m;
            prices[k] = Math.Clamp(Round(line + bridge), day.Low, day.High);
        }

        prices[0] = day.Open;
        prices[steps] = day.Close;
        var highBucket = Enumerable.Range(1, steps).MaxBy(k => prices[k]);
        var lowBucket = Enumerable.Range(1, steps).MinBy(k => prices[k]);

        // U-shaped over the whole session; a forming session spreads its volume so far over the elapsed bars.
        var weights = Enumerable.Range(1, steps).Select(k => 1m + (1.5m * Square((2m * k / total) - 1m))).ToArray();
        var weightSum = weights.Sum();
        var bars = new List<ProviderBar>(elapsed);
        for (var k = 1; k <= elapsed; k++)
        {
            var open = prices[k - 1];
            var close = prices[k];
            var high = k == highBucket ? day.High : Math.Max(open, close);
            var low = k == lowBucket ? day.Low : Math.Min(open, close);
            var volume = (long)(day.Volume * weights[k - 1] / weightSum);
            var start = session.OpenAt.Value.AddMinutes((k - 1) * minutes);
            var isFinal = start.AddMinutes(minutes) <= until;
            bars.Add(new ProviderBar(symbol, interval, start, session.SessionDate, open, high, low, close, volume, isFinal));
        }

        return bars;
    }

    private static decimal BaseVolatility(FixtureInstrument instrument) => 0.008m + (0.006m * (instrument.Beta ?? 1m));

    /// <summary>Daily drift and volatility for a pattern, by how many sessions before the current one the day is.</summary>
    private static (decimal Drift, decimal Volatility) Regime(string pattern, int sessionsAgo, decimal vol) => pattern switch
    {
        "breakout" => sessionsAgo < 30 ? (0m, vol * 0.8m) : (0.0022m, vol),
        "momentum" => sessionsAgo < 60 ? (0.0035m, vol * 0.7m) : (0.001m, vol),
        "pullback" => sessionsAgo < 8 ? (-0.009m, vol * 0.5m) : sessionsAgo < 120 ? (0.0025m, vol * 0.8m) : (0.0005m, vol),
        "earnings" => sessionsAgo < 10 ? (0.003m, vol * 0.7m) : (0.0005m, vol),
        "trend" => (0.0008m, vol * 0.8m),
        "downtrend" => (-0.0012m, vol),
        "flat" => (0m, vol * 0.7m),
        _ => (0m, vol),
    };

    /// <summary>
    /// History averages <c>avg_volume</c>, so the design's relative volumes (NVDA 2.4×, CRM 0.9×) are today's volume
    /// over the fixture average. Only the earnings gap day is heavier.
    /// </summary>
    private static decimal VolumeRegime(string pattern, int sessionsAgo) => pattern == "earnings" && sessionsAgo == 10 ? 3m : 1m;

    /// <summary>Keeps rounding from breaking OHLC order: high is the maximum and low the minimum of the four.</summary>
    private static DailyBar Normalize(ProviderSession session, decimal open, decimal high, decimal low, decimal close, long volume) =>
        new(session, open, Math.Max(high, Math.Max(open, close)), Math.Max(Math.Min(low, Math.Min(open, close)), Cent), close, Math.Max(volume, 0));

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal Square(decimal value) => value * value;
}
