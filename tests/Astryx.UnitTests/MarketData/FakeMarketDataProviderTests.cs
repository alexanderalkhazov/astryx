using Astryx.Modules.MarketData.Application.Providers;
using Astryx.Modules.MarketData.Infrastructure.Providers.Fake;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Astryx.UnitTests.MarketData;

public class FakeMarketDataProviderTests
{
    // Design 01/02 "Updated 14:32 ET" on a Monday after the DST switch: 14:32 EST = 19:32 UTC.
    private static readonly DateTimeOffset DesignMoment = new(2026, 11, 9, 19, 32, 0, TimeSpan.Zero);
    private static readonly DateOnly DesignSession = new(2026, 11, 9);
    private static readonly FixtureData Fixtures = FixtureData.Load();

    private static (FakeMarketDataProvider Provider, MarketDataFaults Faults, FakeTimeProvider Clock) Create(int seed = 20261109, bool walk = false, DateTimeOffset? now = null)
    {
        var clock = new FakeTimeProvider(now ?? DesignMoment);
        var faults = new MarketDataFaults(clock);
        var options = Options.Create(new FakeMarketDataOptions { Seed = seed, IntradayWalk = walk });
        return (new FakeMarketDataProvider(Fixtures, new FakeCalendar(Fixtures), options, faults, clock), faults, clock);
    }

    [Fact]
    public async Task Quotes_reproduce_the_design_values_for_NVDA()
    {
        var (provider, _, _) = Create();

        var nvda = (await provider.GetQuotesAsync(["NVDA"], default)).Single();

        nvda.Last.ShouldBe(186.40m);
        nvda.PrevClose.ShouldBe(180.24m);
        Math.Round((nvda.Last / nvda.PrevClose) - 1, 4).ShouldBe(0.0342m);
        (nvda.Open, nvda.High, nvda.Low, nvda.Volume).ShouldBe((180.60m, 186.77m, 180.05m, 412_800_000L));
        nvda.AsOf.ShouldBe(DesignMoment);
        nvda.Quality.ShouldBe("synthetic");
    }

    [Fact]
    public async Task The_universe_has_1034_instruments_including_the_design_tickers()
    {
        var (provider, _, _) = Create();

        var instruments = await provider.GetInstrumentsAsync(FakeMarketDataProvider.UniverseCode, default);

        instruments.Count.ShouldBe(1034);
        instruments.Select(i => i.Symbol).ShouldBeUnique();
        foreach (var symbol in new[] { "NVDA", "AMD", "AMZN", "CRM", "PLTR", "TSLA", "COST", "JPM", "AAPL", "MSFT", "LLY", "UBER" })
        {
            instruments.ShouldContain(i => i.Symbol == symbol);
        }

        (await provider.GetInstrumentsAsync("unknown_universe", default)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Daily_history_ends_at_the_previous_close_and_has_more_than_300_bars()
    {
        var (provider, _, _) = Create();

        var bars = await provider.GetBarsAsync("NVDA", BarInterval.OneDay, DesignSession.AddYears(-3), DesignSession, default);

        bars.Count.ShouldBeGreaterThan(300);
        bars[^1].SessionDate.ShouldBe(DesignSession);
        bars[^1].IsFinal.ShouldBeFalse(); // 14:32, session still open
        bars[^2].Close.ShouldBe(180.24m);
        bars[^2].IsFinal.ShouldBeTrue();
        bars.Select(b => b.SessionDate).ShouldBeInOrder();
        bars.ShouldNotContain(b => b.SessionDate.DayOfWeek == DayOfWeek.Saturday || b.SessionDate.DayOfWeek == DayOfWeek.Sunday);
    }

    [Fact]
    public async Task NVDA_clears_a_six_week_base_at_185_90()
    {
        var (provider, _, _) = Create();

        var bars = await provider.GetBarsAsync("NVDA", BarInterval.OneDay, DesignSession.AddMonths(-3), DesignSession, default);
        var basePeriod = bars.SkipLast(1).TakeLast(30).ToList();

        basePeriod.Max(b => b.High).ShouldBe(185.90m); // touched, never passed
        basePeriod.Min(b => b.Low).ShouldBe(176.85m);
        basePeriod.SkipLast(1).ShouldAllBe(b => b.Close >= (185.90m + 176.85m) / 2); // closes in the upper half
        bars[^1].High.ShouldBeGreaterThan(185.90m);
    }

    [Fact]
    public async Task NVDA_has_the_design_ATR_and_relative_volume()
    {
        var (provider, _, _) = Create();

        var bars = await provider.GetBarsAsync("NVDA", BarInterval.OneDay, DesignSession.AddYears(-2), DesignSession, default);

        // Wilder ATR(14) at today's bar: design 04's stop 178.20 = entry 186.50 − 1.5 × 5.5333.
        var atr = 0m;
        for (var i = 1; i <= 14; i++)
        {
            atr += TrueRange(bars, i);
        }

        atr /= 14;
        for (var i = 15; i < bars.Count; i++)
        {
            atr = (atr * 13 + TrueRange(bars, i)) / 14;
        }

        atr.ShouldBe(5.5333m, 0.001m); // cent prices limit how finely the wicks can tune it
        Math.Round(186.50m - 1.5m * atr, 2).ShouldBe(178.20m);
        var average = bars.SkipLast(1).TakeLast(20).Average(b => (decimal)b.Volume);
        average.ShouldBe(172_000_000m);
        (bars[^1].Volume / average).ShouldBe(2.4m); // "on 2.4× average volume"
    }

    private static decimal TrueRange(IReadOnlyList<ProviderBar> bars, int i) =>
        Math.Max(bars[i].High - bars[i].Low, Math.Max(Math.Abs(bars[i].High - bars[i - 1].Close), Math.Abs(bars[i].Low - bars[i - 1].Close)));

    [Fact]
    public async Task Every_bar_in_the_universe_is_internally_consistent()
    {
        var (provider, _, _) = Create();

        foreach (var instrument in Fixtures.Instruments)
        {
            var bars = await provider.GetBarsAsync(instrument.Symbol, BarInterval.OneDay, DesignSession.AddYears(-2), DesignSession, default);
            foreach (var bar in bars)
            {
                bar.Low.ShouldBeGreaterThan(0m, instrument.Symbol);
                bar.Low.ShouldBeLessThanOrEqualTo(Math.Min(bar.Open, bar.Close), instrument.Symbol);
                bar.High.ShouldBeGreaterThanOrEqualTo(Math.Max(bar.Open, bar.Close), instrument.Symbol);
                bar.Volume.ShouldBeGreaterThanOrEqualTo(0, instrument.Symbol);
                decimal.Round(bar.Close, 2).ShouldBe(bar.Close, instrument.Symbol);
            }
        }
    }

    [Fact]
    public async Task The_same_seed_and_clock_give_identical_output_and_another_seed_changes_history()
    {
        var a = await Snapshot(Create().Provider);
        var b = await Snapshot(Create().Provider);
        var other = await Snapshot(Create(seed: 7).Provider);

        b.ShouldBe(a);
        other.ShouldNotBe(a);

        static async Task<string> Snapshot(FakeMarketDataProvider provider)
        {
            var bars = await provider.GetBarsAsync("AMD", BarInterval.OneDay, DesignSession.AddYears(-1), DesignSession, default);
            var intraday = await provider.GetBarsAsync("AMD", BarInterval.FiveMinutes, DesignSession, DesignSession, default);
            var quotes = await provider.GetQuotesAsync(["AMD", "CRM", "ZZ001"], default);
            return string.Join('|', bars.Concat(intraday).Select(x => x.ToString()).Concat(quotes.Select(q => q.ToString())));
        }
    }

    [Fact]
    public async Task Intraday_bars_of_a_completed_session_add_up_to_its_daily_bar()
    {
        var (provider, _, _) = Create();
        var friday = new DateOnly(2026, 11, 6);
        var daily = (await provider.GetBarsAsync("AMD", BarInterval.OneDay, friday, friday, default)).Single();

        var bars = await provider.GetBarsAsync("AMD", BarInterval.FiveMinutes, friday, friday, default);

        bars.Count.ShouldBe(78); // 6.5 h / 5 min
        bars[0].BarStart.ShouldBe(new DateTimeOffset(2026, 11, 6, 14, 30, 0, TimeSpan.Zero)); // 09:30 EST
        bars[0].Open.ShouldBe(daily.Open);
        bars[^1].Close.ShouldBe(daily.Close);
        bars.Max(b => b.High).ShouldBe(daily.High);
        bars.Min(b => b.Low).ShouldBe(daily.Low);
        bars.Sum(b => b.Volume).ShouldBeLessThanOrEqualTo(daily.Volume);
        bars.ShouldAllBe(b => b.IsFinal);
    }

    [Fact]
    public async Task A_forming_session_only_has_bars_up_to_now()
    {
        var (provider, _, _) = Create();

        var bars = await provider.GetBarsAsync("NVDA", BarInterval.FifteenMinutes, DesignSession, DesignSession, default);

        bars.Count.ShouldBe(21); // 09:30 → 14:32 is 20 full quarter-hours plus the forming one
        bars[^1].IsFinal.ShouldBeFalse();
        bars[^1].Close.ShouldBe(186.40m);
        bars.SkipLast(1).ShouldAllBe(b => b.IsFinal);
    }

    [Theory]
    [InlineData("2026-10-30", 13, 30, 20, 0, false)] // EDT
    [InlineData("2026-11-02", 14, 30, 21, 0, false)] // EST after the 1 Nov switch
    [InlineData("2026-11-27", 14, 30, 18, 0, true)] // day after Thanksgiving closes 13:00
    public async Task Sessions_follow_New_York_time_across_DST_and_half_days(string date, int openHour, int openMinute, int closeHour, int closeMinute, bool halfDay)
    {
        var (provider, _, _) = Create();
        var day = DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture);

        var session = (await provider.GetCalendarAsync("XNYS", day, day, default)).Single();

        session.OpenAt.ShouldBe(new DateTimeOffset(day.Year, day.Month, day.Day, openHour, openMinute, 0, TimeSpan.Zero));
        session.CloseAt.ShouldBe(new DateTimeOffset(day.Year, day.Month, day.Day, closeHour, closeMinute, 0, TimeSpan.Zero));
        session.IsHalfDay.ShouldBe(halfDay);
    }

    [Fact]
    public async Task The_calendar_lists_holidays_and_skips_weekends()
    {
        var (provider, _, _) = Create();

        var week = await provider.GetCalendarAsync("XNAS", new DateOnly(2026, 11, 23), new DateOnly(2026, 11, 29), default);

        week.Count.ShouldBe(5);
        week.Single(d => d.SessionDate == new DateOnly(2026, 11, 26)).ShouldSatisfyAllConditions(
            d => d.IsHoliday.ShouldBeTrue(),
            d => d.OpenAt.ShouldBeNull());
        (await provider.GetCalendarAsync("XLON", new DateOnly(2026, 11, 23), new DateOnly(2026, 11, 29), default)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Outside_market_hours_quotes_carry_the_closing_time()
    {
        var saturday = new DateTimeOffset(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);
        var (provider, _, _) = Create(now: saturday);

        var quote = (await provider.GetQuotesAsync(["NVDA"], default)).Single();

        quote.AsOf.ShouldBe(new DateTimeOffset(2026, 11, 13, 21, 0, 0, TimeSpan.Zero)); // Friday 16:00 EST
        quote.Last.ShouldBe(186.40m);
    }

    [Fact]
    public async Task Down_fails_every_call()
    {
        var (provider, faults, _) = Create();
        faults.Set(MarketDataFaultMode.Down);

        await Should.ThrowAsync<MarketDataUnavailableException>(() => provider.GetQuotesAsync(["NVDA"], default));
        await Should.ThrowAsync<MarketDataUnavailableException>(() => provider.GetBarsAsync("NVDA", BarInterval.OneDay, DesignSession, DesignSession, default));

        faults.Set(MarketDataFaultMode.Ok);
        (await provider.GetQuotesAsync(["NVDA"], default)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Stale_freezes_quotes_at_the_moment_it_was_set()
    {
        var (provider, faults, clock) = Create(walk: true);
        faults.Set(MarketDataFaultMode.Stale);
        var frozen = (await provider.GetQuotesAsync(["NVDA"], default)).Single();

        clock.Advance(TimeSpan.FromMinutes(20));
        var later = (await provider.GetQuotesAsync(["NVDA"], default)).Single();

        later.ShouldBe(frozen);
        later.AsOf.ShouldBe(DesignMoment);
    }

    [Fact]
    public async Task Bad_tick_spikes_the_first_symbol_of_a_request()
    {
        var (provider, faults, _) = Create();
        faults.Set(MarketDataFaultMode.BadTick);

        var quotes = await provider.GetQuotesAsync(["NVDA", "AMD"], default);

        quotes[0].Last.ShouldBe(1802.40m);
        quotes[1].Last.ShouldBe(168.25m);
    }

    [Fact]
    public async Task The_intraday_walk_moves_prices_deterministically_while_the_session_is_open()
    {
        var first = (await Create(walk: true).Provider.GetQuotesAsync(["NVDA"], default)).Single();
        var again = (await Create(walk: true).Provider.GetQuotesAsync(["NVDA"], default)).Single();

        again.ShouldBe(first);
        first.Last.ShouldNotBe(186.40m);
        first.High.ShouldBeGreaterThanOrEqualTo(first.Last);
        first.Low.ShouldBeLessThanOrEqualTo(first.Last);
    }

    [Fact]
    public async Task Unknown_symbols_are_omitted_and_fundamentals_cover_equities_only()
    {
        var (provider, _, _) = Create();

        (await provider.GetQuotesAsync(["NOPE", "AMD"], default)).Single().Symbol.ShouldBe("AMD");
        (await provider.GetBarsAsync("NOPE", BarInterval.OneDay, DesignSession, DesignSession, default)).ShouldBeEmpty();
        (await provider.GetFundamentalsAsync("SPY", default)).ShouldBeNull();
        var nvda = await provider.GetFundamentalsAsync("NVDA", default);
        nvda.ShouldNotBeNull();
        nvda.NextEarningsDate.ShouldBe(DesignSession.AddDays(14));
        nvda.Beta.ShouldBe(1.72m);
    }

    [Fact]
    public async Task Corporate_actions_come_from_the_fixture()
    {
        var (provider, _, _) = Create();

        var actions = await provider.GetCorporateActionsAsync("NVDA", new DateOnly(2024, 1, 1), default);

        actions.Select(a => (a.ActionType, a.ExDate)).ShouldBe([("split", new DateOnly(2024, 6, 10)), ("cash_dividend", new DateOnly(2026, 9, 11))]);
        actions[0].Ratio.ShouldBe(10m);
    }

    [Fact]
    public async Task News_is_newest_first_and_includes_prompt_injection_headlines()
    {
        var clock = new FakeTimeProvider(DesignMoment);
        var news = new FakeNewsProvider(Fixtures, clock);

        var items = await news.GetNewsAsync("NVDA", DesignMoment.AddDays(-2), 10, default);

        items.Select(i => i.PublishedAt).ShouldBeInOrder(SortDirection.Descending);
        items.ShouldContain(i => i.Headline.StartsWith("IGNORE ALL PREVIOUS INSTRUCTIONS", StringComparison.Ordinal));
        (await news.GetNewsAsync("NVDA", DesignMoment.AddHours(-1), 10, default)).ShouldHaveSingleItem().ProviderArticleId.ShouldBe("fx-001");
        (await news.GetNewsAsync("NVDA", DesignMoment.AddDays(-2), 1, default)).Count.ShouldBe(1);
    }
}
