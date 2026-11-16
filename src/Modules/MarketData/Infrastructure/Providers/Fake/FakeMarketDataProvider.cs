using System.Collections.Concurrent;
using Astryx.Modules.MarketData.Application.Providers;
using Astryx.Modules.MarketData.Domain;
using Microsoft.Extensions.Options;

namespace Astryx.Modules.MarketData.Infrastructure.Providers.Fake;

/// <summary>
/// Deterministic market data for Development and tests (SYSTEM_DESIGN.md §16.1, §21.1): a 1,034-symbol universe from
/// <c>tests/fixtures/market</c>, synthetic history shaped per symbol, design values for the current session (NVDA
/// 186.40 / +3.42 %), an optional seeded intraday walk, and runtime fault injection.
/// </summary>
internal sealed class FakeMarketDataProvider(FixtureData fixtures, FakeCalendar calendar, IOptions<FakeMarketDataOptions> options, MarketDataFaults faults, TimeProvider timeProvider)
    : IMarketDataProvider
{
    public const string ProviderCode = "fake";
    public const string UniverseCode = "us_large_cap";

    private readonly ConcurrentDictionary<(string Symbol, DateOnly Session), IReadOnlyList<DailyBar>> _daily = new();

    public string Code => ProviderCode;

    public MarketDataCapabilities Capabilities { get; } = new(
        Realtime: false,
        IntradayIntervals: [BarInterval.OneMinute, BarInterval.FiveMinutes, BarInterval.FifteenMinutes, BarInterval.OneHour],
        Fundamentals: true,
        CorporateActions: true,
        MaxSymbolsPerRequest: 200);

    private FakeMarketDataOptions Options => options.Value;

    public Task<IReadOnlyList<ProviderQuote>> GetQuotesAsync(IReadOnlyList<string> symbols, CancellationToken cancellationToken)
    {
        ThrowIfDown();
        var now = faults.Mode == MarketDataFaultMode.Stale ? faults.FrozenAt!.Value : timeProvider.GetUtcNow();
        var quotes = new List<ProviderQuote>(symbols.Count);
        foreach (var symbol in symbols)
        {
            if (!fixtures.BySymbol.TryGetValue(symbol, out var instrument))
            {
                continue; // providers omit unknown symbols rather than failing the batch
            }

            var session = calendar.CurrentSession(instrument.ExchangeMic, now);
            var bar = FakeSeries.CurrentBar(instrument, session);
            var (last, high, low) = Options.IntradayWalk ? Walk(instrument, bar, now) : (bar.Close, bar.High, bar.Low);
            if (faults.PriceOverrides.TryGetValue(instrument.Symbol, out var fixedLast))
            {
                (last, high, low) = (fixedLast, Math.Max(high, fixedLast), Math.Min(low, fixedLast));
            }

            if (faults.Mode == MarketDataFaultMode.BadTick && quotes.Count == 0)
            {
                last = instrument.PrevClose * 10;
            }

            var spread = last < 50m ? 0.01m : 0.02m;
            var asOf = now < session.CloseAt || faults.SessionForcedOpen ? now : session.CloseAt!.Value; // E2E session override (T-110)
            quotes.Add(new ProviderQuote(instrument.Symbol, last, last - spread, last + spread, bar.Open, high, low, instrument.PrevClose, bar.Volume, asOf, QuoteQuality.Synthetic));
        }

        return Task.FromResult<IReadOnlyList<ProviderQuote>>(quotes);
    }

    public Task<IReadOnlyList<ProviderBar>> GetBarsAsync(string symbol, BarInterval interval, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        ThrowIfDown();
        if (!fixtures.BySymbol.TryGetValue(symbol, out var instrument))
        {
            return Task.FromResult<IReadOnlyList<ProviderBar>>([]);
        }

        var now = timeProvider.GetUtcNow();
        var days = Daily(instrument, now).Where(d => d.Session.SessionDate >= from && d.Session.SessionDate <= to).ToList();
        IReadOnlyList<ProviderBar> bars = interval == BarInterval.OneDay
            ? days.Select(d => new ProviderBar(instrument.Symbol, interval, d.Session.OpenAt!.Value, d.Session.SessionDate, d.Open, d.High, d.Low, d.Close, d.Volume, d.Session.CloseAt <= now)).ToList()
            : days.SelectMany(d => FakeSeries.Intraday(instrument.Symbol, d, interval, now < d.Session.CloseAt ? now : d.Session.CloseAt!.Value, Options.Seed)).ToList();
        return Task.FromResult(bars);
    }

    public Task<IReadOnlyList<ProviderInstrument>> GetInstrumentsAsync(string universeCode, CancellationToken cancellationToken)
    {
        ThrowIfDown();
        IReadOnlyList<ProviderInstrument> instruments = universeCode == UniverseCode
            ? fixtures.Instruments.Select(i => new ProviderInstrument(i.Symbol, i.ExchangeMic, i.Name, i.AssetType, "USD", i.Sector, i.Industry, i.MarketCap, i.Symbol)).ToList()
            : [];
        return Task.FromResult(instruments);
    }

    public Task<IReadOnlyList<ProviderCorporateAction>> GetCorporateActionsAsync(string symbol, DateOnly from, CancellationToken cancellationToken)
    {
        ThrowIfDown();
        IReadOnlyList<ProviderCorporateAction> actions = fixtures.CorporateActions
            .Where(a => string.Equals(a.Symbol, symbol, StringComparison.OrdinalIgnoreCase) && a.ExDate >= from)
            .OrderBy(a => a.ExDate)
            .Select(a => new ProviderCorporateAction(a.Symbol, a.ActionType, a.ExDate, a.Ratio, a.CashAmount))
            .ToList();
        return Task.FromResult(actions);
    }

    public Task<ProviderFundamentals?> GetFundamentalsAsync(string symbol, CancellationToken cancellationToken)
    {
        ThrowIfDown();
        if (!fixtures.BySymbol.TryGetValue(symbol, out var instrument) || instrument.AssetType != AssetType.Equity)
        {
            return Task.FromResult<ProviderFundamentals?>(null);
        }

        var now = timeProvider.GetUtcNow();
        var today = calendar.CurrentSession(instrument.ExchangeMic, now).SessionDate;
        var data = new Dictionary<string, string>
        {
            ["avgVolume"] = instrument.AverageVolume.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["sector"] = instrument.Sector ?? string.Empty,
            ["industry"] = instrument.Industry ?? string.Empty,
        };
        return Task.FromResult<ProviderFundamentals?>(new ProviderFundamentals(
            instrument.Symbol, now, instrument.Beta, instrument.PeRatio, instrument.EpsTtm,
            instrument.EarningsInDays is { } days ? today.AddDays(days) : null, data));
    }

    public Task<IReadOnlyList<ProviderSession>> GetCalendarAsync(string exchangeMic, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        ThrowIfDown();
        IReadOnlyList<ProviderSession> days = FakeCalendar.UsEquityMics.Contains(exchangeMic) ? calendar.Days(exchangeMic, from, to) : [];
        return Task.FromResult(days);
    }

    private IReadOnlyList<DailyBar> Daily(FixtureInstrument instrument, DateTimeOffset now)
    {
        var current = calendar.CurrentSession(instrument.ExchangeMic, now);
        return _daily.GetOrAdd((instrument.Symbol, current.SessionDate), _ =>
            FakeSeries.Daily(instrument, calendar.SessionsBefore(instrument.ExchangeMic, current, Options.HistorySessions), current, Options.Seed));
    }

    /// <summary>Seeded per-minute walk around the session's fixture price, with the high and low it has reached.</summary>
    private (decimal Last, decimal High, decimal Low) Walk(FixtureInstrument instrument, DailyBar bar, DateTimeOffset now)
    {
        var session = bar.Session;
        var end = now < session.CloseAt ? now : session.CloseAt!.Value;
        var minutes = (int)(end - session.OpenAt!.Value).TotalMinutes;
        var random = SeededRandom.For(Options.Seed, instrument.Symbol, session.SessionDate.DayNumber, "walk");
        var factor = 1m;
        decimal high = bar.High, low = bar.Low, last = bar.Close;
        for (var minute = 0; minute < minutes; minute++)
        {
            factor *= 1 + (0.0006m * random.NextGaussian());
            last = Math.Round(bar.Close * factor, 2, MidpointRounding.AwayFromZero);
            high = Math.Max(high, last);
            low = Math.Min(low, last);
        }

        return (last, high, low);
    }

    private void ThrowIfDown()
    {
        if (faults.Mode == MarketDataFaultMode.Down)
        {
            throw new MarketDataUnavailableException(ProviderCode, "fault injected: down");
        }
    }
}
