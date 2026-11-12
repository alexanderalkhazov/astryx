using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astryx.Modules.MarketData.Application.Ingestion;
using Astryx.Modules.MarketData.Application.Market;
using Astryx.Modules.MarketData.Application.Providers;
using Astryx.Modules.MarketData.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Astryx.IntegrationTests;

/// <summary>Session state and freshness budgets at chosen instants (SYSTEM_DESIGN.md §20.4, §20.5, T-043).</summary>
public class MarketSessionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    // Friday before the DST switch (EDT, UTC−4): 09:29 is pre-market.
    [InlineData("2026-10-30T13:29:00Z", "Pre", "2026-10-30", "2026-10-30T13:30:00Z", "2026-10-30T20:00:00Z", false)]
    // Monday after the 1 Nov switch (EST, UTC−5): the same wall-clock hours are an hour later in UTC.
    [InlineData("2026-11-02T14:30:00Z", "Open", "2026-11-02", "2026-11-02T14:30:00Z", "2026-11-02T21:00:00Z", false)]
    [InlineData("2026-11-02T21:30:00Z", "Post", "2026-11-02", "2026-11-02T14:30:00Z", "2026-11-02T21:00:00Z", false)]
    [InlineData("2026-11-03T01:30:00Z", "Closed", "2026-11-03", "2026-11-03T14:30:00Z", "2026-11-03T21:00:00Z", false)] // 20:30 ET
    [InlineData("2026-11-02T08:00:00Z", "Closed", "2026-11-02", "2026-11-02T14:30:00Z", "2026-11-02T21:00:00Z", false)] // 03:00 ET
    // Thanksgiving is closed; the next session is the half day that closes at 13:00.
    [InlineData("2026-11-26T17:00:00Z", "Closed", "2026-11-27", "2026-11-27T14:30:00Z", "2026-11-27T18:00:00Z", true)]
    [InlineData("2026-11-27T18:30:00Z", "Post", "2026-11-27", "2026-11-27T14:30:00Z", "2026-11-27T18:00:00Z", true)]
    [InlineData("2026-11-27T22:30:00Z", "Closed", "2026-11-30", "2026-11-30T14:30:00Z", "2026-11-30T21:00:00Z", false)] // 17:30 ET: half-day post is over
    [InlineData("2026-11-28T15:00:00Z", "Closed", "2026-11-30", "2026-11-30T14:30:00Z", "2026-11-30T21:00:00Z", false)] // Saturday
    // Spring forward: 14 Mar 2027.
    [InlineData("2027-03-12T15:00:00Z", "Open", "2027-03-12", "2027-03-12T14:30:00Z", "2027-03-12T21:00:00Z", false)]
    [InlineData("2027-03-15T13:30:00Z", "Open", "2027-03-15", "2027-03-15T13:30:00Z", "2027-03-15T20:00:00Z", false)]
    public async Task Session_state_follows_New_York_time(string now, string state, string sessionDate, string opensAt, string closesAt, bool halfDay)
    {
        await using var scope = Scope(DateTimeOffset.Parse(now), out var services);

        var session = await services.GetRequiredService<MarketSessionService>().GetAsync(default);

        session.State.ToString().ShouldBe(state);
        session.SessionDate.ShouldBe(DateOnly.Parse(sessionDate));
        session.OpensAt.ShouldBe(DateTimeOffset.Parse(opensAt));
        session.ClosesAt.ShouldBe(DateTimeOffset.Parse(closesAt));
        session.IsHalfDay.ShouldBe(halfDay);
    }

    [Theory]
    [InlineData("2026-11-09T19:32:00Z", "2026-11-09T19:31:00Z", false)] // in session, 60 s old
    [InlineData("2026-11-09T19:32:00Z", "2026-11-09T19:30:00Z", true)] // in session, 120 s old
    [InlineData("2026-11-09T23:00:00Z", "2026-11-09T21:00:00Z", false)] // after the close, at the close
    [InlineData("2026-11-09T23:00:00Z", "2026-11-09T20:50:00Z", true)] // after the close, ten minutes before it
    [InlineData("2026-11-14T15:00:00Z", "2026-11-13T21:00:00Z", false)] // weekend, Friday's close
    public async Task Quotes_are_fresh_within_90_seconds_in_session_and_at_the_close_otherwise(string now, string asOf, bool stale)
    {
        await using var scope = Scope(DateTimeOffset.Parse(now), out var services);

        var freshness = await services.GetRequiredService<FreshnessPolicy>().QuoteAsync(DateTimeOffset.Parse(asOf), default);

        freshness.Stale.ShouldBe(stale);
        freshness.AsOf.ShouldBe(DateTimeOffset.Parse(asOf));
    }

    [Theory]
    [InlineData("2026-11-09T22:59:00Z", "2026-11-06", false)] // 17:59 ET Monday: Friday's bar is enough
    [InlineData("2026-11-09T23:00:00Z", "2026-11-06", true)] // 18:00 ET: Monday's bar is now due
    [InlineData("2026-11-09T23:00:00Z", "2026-11-09", false)]
    [InlineData("2026-11-27T23:30:00Z", "2026-11-25", true)] // the half day still has an 18:00 deadline
    public async Task Daily_bars_must_include_the_last_session_by_18_00_ET(string now, string lastBar, bool stale)
    {
        await using var scope = Scope(DateTimeOffset.Parse(now), out var services);

        (await services.GetRequiredService<FreshnessPolicy>().DailyBarsStaleAsync(DateOnly.Parse(lastBar), default)).ShouldBe(stale);
    }

    [Fact]
    public async Task A_missing_quote_is_stale()
    {
        await using var scope = Scope(DateTimeOffset.Parse("2026-11-09T19:32:00Z"), out var services);

        (await services.GetRequiredService<FreshnessPolicy>().QuoteAsync(null, default)).ShouldBe(new Freshness(null, true));
    }

    [Fact]
    public async Task Session_endpoint_requires_a_session_and_reports_the_state()
    {
        (await factory.CreateClient().GetAsync("/api/v1/market/session")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var client = await TestUsers.SignedInClientAsync(factory);
        var body = await client.GetFromJsonAsync<JsonElement>("/api/v1/market/session");

        body.GetProperty("exchange").GetString().ShouldBe("XNYS");
        body.GetProperty("state").GetString().ShouldBeOneOf("open", "closed", "pre", "post");
        body.GetProperty("opensAt").GetString()!.ShouldEndWith("Z");
        body.GetProperty("closesAt").GetDateTimeOffset().ShouldBeGreaterThan(body.GetProperty("opensAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task System_status_reports_market_data_freshness()
    {
        var body = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/system/status");

        var market = body.GetProperty("marketData");
        market.GetProperty("provider").GetString().ShouldBe("Fake");
        market.GetProperty("quotesStale").GetBoolean().ShouldBeTrue(); // nothing synced in a test database
        market.GetProperty("quotesAsOf").ValueKind.ShouldBe(JsonValueKind.Null);
        market.GetProperty("dailyBarsStale").GetBoolean().ShouldBeTrue();
    }

    /// <summary>Services bound to a clock of our choosing; the calendar comes from the fake provider.</summary>
    private AsyncServiceScope Scope(DateTimeOffset now, out IServiceProvider services)
    {
        _ = factory.CreateClient();
        var scope = factory.Services.CreateAsyncScope();
        var clock = new MarketClock(scope.ServiceProvider.GetRequiredService<MarketDataDbContext>(), scope.ServiceProvider.GetRequiredService<IMarketDataProvider>(), new FakeTimeProvider(now));
        var collection = new ServiceCollection();
        collection.AddSingleton(clock);
        collection.AddSingleton<MarketSessionService>();
        collection.AddSingleton(scope.ServiceProvider.GetRequiredService<MarketDataDbContext>());
        collection.AddSingleton(scope.ServiceProvider.GetRequiredService<IOptions<FreshnessOptions>>());
        collection.AddSingleton<FreshnessPolicy>();
        services = collection.BuildServiceProvider();
        return scope;
    }
}
