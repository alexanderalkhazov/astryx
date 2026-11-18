using System.Net;
using System.Text;
using Astryx.Infrastructure.Alpaca;
using Astryx.Modules.MarketData.Application.Providers;
using Astryx.Modules.MarketData.Domain;
using Astryx.Modules.MarketData.Infrastructure.Providers.Alpaca;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Astryx.UnitTests.MarketData;

/// <summary>Alpaca market data contract (SYSTEM_DESIGN.md §16.2, §17.3, TASKS.md T-147), against Alpaca's documented shapes.</summary>
public class AlpacaMarketDataProviderTests
{
    /// <summary>Answers by path and query, and records every request.</summary>
    private sealed class StubAlpaca(Func<Uri, (HttpStatusCode Status, string Body)> answer) : HttpMessageHandler, IHttpClientFactory
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var (status, body) = answer(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    // Monday 2026-11-16, 15:00 New York (20:00 UTC): the session is open.
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 20, 0, 0, TimeSpan.Zero);

    private static (AlpacaMarketDataProvider Provider, StubAlpaca Stub) Create(Func<Uri, (HttpStatusCode, string)> answer)
    {
        var stub = new StubAlpaca(answer);
        var options = Options.Create(new AlpacaOptions { KeyId = "PKTEST00003F2A", SecretKey = "secret-value" });
        return (new AlpacaMarketDataProvider(stub, new AlpacaRateLimiter(options), options, AlpacaUniverse.Load(), new FakeTimeProvider(Now)), stub);
    }

    private const string IexSnapshots = """
        {"NVDA":{"latestTrade":{"p":156.66,"t":"2026-11-16T19:59:58Z"},"latestQuote":{"bp":156.64,"ap":156.68,"t":"2026-11-16T19:59:59Z"},"dailyBar":{"o":160.1,"h":161,"l":155,"c":156.6,"v":11877170}},
         "AAPL":{"latestTrade":{"p":352.66,"t":"2026-11-16T19:59:50Z"},"latestQuote":{"bp":339.11,"ap":375.22,"t":"2026-11-16T19:59:59Z"}}}
        """;

    private const string DelayedSnapshots = """
        {"NVDA":{"dailyBar":{"o":160.12,"h":161.2,"l":155.4,"c":155.6,"v":334760103,"t":"2026-11-16T05:00:00Z"},"prevDailyBar":{"c":162.85}},
         "AAPL":{"dailyBar":{"o":350,"h":353.87,"l":346.46,"c":352.7,"v":29910098},"prevDailyBar":{"c":348.51}}}
        """;

    [Fact]
    public async Task Quotes_take_price_from_realtime_IEX_and_volume_and_the_day_from_the_delayed_consolidated_feed()
    {
        var (provider, stub) = Create(uri => (HttpStatusCode.OK, uri.Query.Contains("feed=iex") ? IexSnapshots : DelayedSnapshots));

        var quotes = await provider.GetQuotesAsync(["NVDA", "AAPL", "UNKNOWN"], default);

        var nvda = quotes.Single(q => q.Symbol == "NVDA");
        (nvda.Last, nvda.Bid, nvda.Ask, nvda.PrevClose, nvda.Volume, nvda.Quality).ShouldBe((156.66m, 156.64m, 156.68m, 162.85m, 334760103L, QuoteQuality.Realtime));
        (nvda.Open, nvda.High, nvda.Low).ShouldBe((160.12m, 161.2m, 155.4m));
        nvda.AsOf.ShouldBe(DateTimeOffset.Parse("2026-11-16T19:59:59Z", System.Globalization.CultureInfo.InvariantCulture)); // the newer of trade and quote
        var aapl = quotes.Single(q => q.Symbol == "AAPL");
        (aapl.Bid, aapl.Ask).ShouldBe((null, null)); // a 10 % wide IEX quote is not a usable spread
        aapl.AsOf.ShouldBe(DateTimeOffset.Parse("2026-11-16T19:59:59Z", System.Globalization.CultureInfo.InvariantCulture)); // the book moved after the last print
        quotes.ShouldNotContain(q => q.Symbol == "UNKNOWN");
        stub.Requests.Count.ShouldBe(2);
        stub.Requests.ShouldAllBe(r => r.Headers.GetValues("APCA-API-KEY-ID").Single() == "PKTEST00003F2A" && !r.RequestUri!.ToString().Contains("secret"));
    }

    [Fact]
    public async Task After_hours_the_quote_is_the_regular_session_close_not_the_last_extended_hours_print()
    {
        // NVDA on 2026-11-17: closed at 155.46, then traded at 158.30 on IEX at 16:59 ET.
        const string iex = """{"NVDA":{"latestTrade":{"p":158.30,"t":"2026-11-17T21:59:41Z"},"latestQuote":{"bp":158.00,"ap":159.74,"t":"2026-11-17T21:59:50Z"}}}""";
        const string delayed = """{"NVDA":{"dailyBar":{"o":156.90,"h":158.19,"l":150.33,"c":155.46,"v":407797122,"t":"2026-11-17T05:00:00Z"},"prevDailyBar":{"c":156.14}}}""";
        var (provider, _) = Create(uri => (HttpStatusCode.OK, uri.Query.Contains("feed=iex") ? iex : delayed));

        var nvda = (await provider.GetQuotesAsync(["NVDA"], default)).Single();

        (nvda.Last, nvda.PrevClose, nvda.High, nvda.Bid, nvda.Ask).ShouldBe((155.46m, 156.14m, 158.19m, (decimal?)null, (decimal?)null));
        nvda.AsOf.ShouldBe(DateTimeOffset.Parse("2026-11-17T21:00:00Z", System.Globalization.CultureInfo.InvariantCulture)); // the bell
    }

    [Fact]
    public async Task An_unknown_symbol_is_dropped_from_the_batch_instead_of_failing_it()
    {
        var (provider, stub) = Create(uri => uri.Query.Contains("ZZ001")
            ? (HttpStatusCode.BadRequest, """{"code":400,"message":"invalid symbol: ZZ001"}""")
            : (HttpStatusCode.OK, uri.Query.Contains("feed=iex") ? IexSnapshots : DelayedSnapshots));

        var quotes = await provider.GetQuotesAsync(["ZZ001", "NVDA"], default);

        quotes.Select(q => q.Symbol).ShouldBe(["NVDA"]);
        stub.Requests.Count.ShouldBe(4); // each feed: one rejected batch, one retry without ZZ001
    }

    [Fact]
    public async Task Daily_bars_are_unadjusted_consolidated_history_dated_by_New_York_session_and_paged()
    {
        var (provider, stub) = Create(uri => (HttpStatusCode.OK, uri.Query.Contains("page_token=next")
            ? """{"bars":{"NVDA":[{"t":"2026-11-16T05:00:00Z","o":160.12,"h":161.2,"l":155.4,"c":155.6,"v":334760103}]},"next_page_token":null}"""
            : """{"bars":{"NVDA":[{"t":"2026-11-13T05:00:00Z","o":170,"h":171,"l":168,"c":169.5,"v":300000000}]},"next_page_token":"next"}"""));

        var bars = await provider.GetBarsAsync("NVDA", BarInterval.OneDay, new DateOnly(2026, 11, 13), new DateOnly(2026, 11, 16), default);

        bars.Count.ShouldBe(2);
        bars[0].SessionDate.ShouldBe(new DateOnly(2026, 11, 13));
        bars[0].BarStart.ShouldBe(new DateTimeOffset(2026, 11, 13, 14, 30, 0, TimeSpan.Zero)); // 09:30 New York
        bars[0].IsFinal.ShouldBeTrue();
        bars[1].IsFinal.ShouldBeFalse(); // today's session is still open
        var first = stub.Requests[0].RequestUri!.Query;
        first.ShouldContain("feed=sip");
        first.ShouldContain("adjustment=raw");
        first.ShouldContain("timeframe=1Day");
        first.ShouldContain("end=2026-11-16T19%3A44%3A00Z"); // clamped to 16 minutes ago: the Basic plan's consolidated delay
    }

    [Fact]
    public async Task Intraday_bars_keep_the_regular_session_only()
    {
        var (provider, _) = Create(_ => (HttpStatusCode.OK, """
            {"bars":{"NVDA":[
              {"t":"2026-11-16T13:00:00Z","o":1,"h":1,"l":1,"c":1,"v":1},
              {"t":"2026-11-16T14:30:00Z","o":160,"h":160.5,"l":159.8,"c":160.2,"v":900000},
              {"t":"2026-11-16T21:30:00Z","o":1,"h":1,"l":1,"c":1,"v":1}]},"next_page_token":null}
            """));

        var bars = await provider.GetBarsAsync("NVDA", BarInterval.FiveMinutes, new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 16), default);

        bars.Single().BarStart.ShouldBe(new DateTimeOffset(2026, 11, 16, 14, 30, 0, TimeSpan.Zero)); // 08:00 and 16:30 New York are dropped
    }

    [Fact]
    public async Task Splits_are_new_over_old_shares_and_dividends_carry_the_cash_rate()
    {
        var (provider, _) = Create(_ => (HttpStatusCode.OK, """
            {"corporate_actions":{
              "forward_splits":[{"symbol":"NVDA","ex_date":"2024-06-10","new_rate":10,"old_rate":1}],
              "reverse_splits":[{"symbol":"NVDA","ex_date":"2025-01-02","new_rate":1,"old_rate":10}],
              "cash_dividends":[{"symbol":"NVDA","ex_date":"2026-09-11","rate":0.01}]},"next_page_token":null}
            """));

        var actions = await provider.GetCorporateActionsAsync("NVDA", new DateOnly(2024, 1, 1), default);

        actions.Select(a => (a.ActionType, a.ExDate, a.Ratio, a.CashAmount)).ShouldBe(
        [
            (CorporateActionType.Split, new DateOnly(2024, 6, 10), 10m, null),
            (CorporateActionType.ReverseSplit, new DateOnly(2025, 1, 2), 0.1m, null),
            (CorporateActionType.CashDividend, new DateOnly(2026, 9, 11), null, 0.01m),
        ]);
    }

    [Fact]
    public async Task The_calendar_marks_half_days_and_adds_holiday_rows_for_missing_weekdays()
    {
        var (provider, stub) = Create(_ => (HttpStatusCode.OK, """
            [{"date":"2026-11-25","open":"09:30","close":"16:00"},{"date":"2026-11-27","open":"09:30","close":"13:00"}]
            """));

        var days = await provider.GetCalendarAsync("XNAS", new DateOnly(2026, 11, 25), new DateOnly(2026, 11, 29), default);

        days.Select(d => (d.SessionDate.Day, d.IsHoliday, d.IsHalfDay)).ShouldBe([(25, false, false), (26, true, false), (27, false, true)]);
        days[2].CloseAt.ShouldBe(new DateTimeOffset(2026, 11, 27, 18, 0, 0, TimeSpan.Zero)); // 13:00 New York
        stub.Requests.Single().RequestUri!.Host.ShouldBe("paper-api.alpaca.markets");
        (await provider.GetCalendarAsync("XLON", new DateOnly(2026, 11, 25), new DateOnly(2026, 11, 29), default)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, """{"message":"unauthorized."}""")]
    [InlineData(HttpStatusCode.Forbidden, """{"message":"subscription does not permit querying recent SIP data"}""")]
    [InlineData(HttpStatusCode.TooManyRequests, """{"message":"too many requests."}""")]
    [InlineData(HttpStatusCode.InternalServerError, "<html>oops</html>")]
    public async Task Failures_surface_as_market_data_unavailable_without_the_secret(HttpStatusCode status, string body)
    {
        var (provider, _) = Create(_ => (status, body));

        var error = await Should.ThrowAsync<MarketDataUnavailableException>(() => provider.GetQuotesAsync(["NVDA"], default));

        error.Provider.ShouldBe("alpaca");
        error.Message.ShouldContain(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture));
        error.Message.ShouldNotContain("secret-value");
    }

    [Fact]
    public void The_universe_is_the_SP500_with_GICS_sectors()
    {
        var universe = AlpacaUniverse.Load();

        universe.Members.Count.ShouldBe(500);
        universe.Members.Select(m => m.Sector).Distinct().Count().ShouldBe(11);
        universe.Members.Single(m => m.Symbol == "NVDA").ShouldBe(new AlpacaUniverse.Member("NVDA", "XNAS", "Nvidia", "Information Technology", "Semiconductors"));
        universe.Members.ShouldContain(m => m.Symbol == "BRK.B");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "unauthorized.", AlpacaFailure.CredentialsInvalid)]
    [InlineData(HttpStatusCode.Forbidden, "forbidden.", AlpacaFailure.CredentialsInvalid)]
    [InlineData(HttpStatusCode.Forbidden, "insufficient buying power", AlpacaFailure.InsufficientFunds)]
    [InlineData(HttpStatusCode.UnprocessableEntity, "qty must be > 0", AlpacaFailure.Invalid)]
    [InlineData(HttpStatusCode.NotFound, "order not found", AlpacaFailure.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests, "too many requests.", AlpacaFailure.RateLimited)]
    [InlineData(HttpStatusCode.BadGateway, "bad gateway", AlpacaFailure.Unavailable)]
    public void Http_failures_map_to_app_failures(HttpStatusCode status, string message, AlpacaFailure expected) =>
        AlpacaHttp.ToException(status, $$"""{"message":"{{message}}"}""").Failure.ShouldBe(expected);

    [Fact]
    public void Credentials_print_only_the_key_suffix() =>
        new AlpacaCredentials("PKLONGKEY3F2A", "do-not-print").ToString().ShouldBe("AlpacaCredentials { KeyId = ••••3F2A }");
}
