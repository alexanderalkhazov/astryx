using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astryx.Infrastructure.Jobs;
using Astryx.Modules.MarketData.Application.Ingestion;
using Astryx.Modules.MarketData.Infrastructure.Persistence;
using Astryx.Modules.MarketData.Infrastructure.Providers.Fake;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.IntegrationTests;

/// <summary>A host whose market tables are synced once from the fake provider on the real clock.</summary>
public class SyncedMarketApiFactory : ApiFactory
{
    private bool _synced;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task EnsureSyncedAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_synced)
            {
                return;
            }

            _ = CreateClient();
            await RunAsync<InstrumentSyncJob, InstrumentSyncHandler>(new InstrumentSyncJob());
            await RunAsync<CalendarSyncJob, CalendarSyncHandler>(new CalendarSyncJob());
            await RunAsync<EodBarSyncJob, EodBarSyncHandler>(new EodBarSyncJob());
            await RunAsync<QuoteSyncJob, QuoteSyncHandler>(new QuoteSyncJob());
            await RunAsync<FundamentalsSyncJob, FundamentalsSyncHandler>(new FundamentalsSyncJob());
            _synced = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Re-syncs quotes so they are inside the freshness budget again (long suites outlive it in session).</summary>
    public Task RefreshQuotesAsync() => RunAsync<QuoteSyncJob, QuoteSyncHandler>(new QuoteSyncJob());

    private async Task RunAsync<TJob, THandler>(TJob job)
        where TJob : IJob
        where THandler : IJobHandler<TJob>
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<THandler>().HandleAsync(job, new JobContext(Guid.NewGuid(), 1, 3, null), default);
    }
}

/// <summary>Market endpoints against synced fixture data (SYSTEM_DESIGN.md §11.4, T-045). Values are the design's, whatever the time.</summary>
public class MarketEndpointTests(SyncedMarketApiFactory factory) : IClassFixture<SyncedMarketApiFactory>, IAsyncLifetime
{
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await factory.EnsureSyncedAsync();
        _client = await TestUsers.SignedInClientAsync(factory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Market_endpoints_require_a_session()
    {
        var anonymous = factory.CreateClient();
        foreach (var path in new[] { "/api/v1/instruments/NVDA", "/api/v1/market/NVDA/quote", "/api/v1/market/NVDA/bars", "/api/v1/market/breadth" })
        {
            (await anonymous.GetAsync(path)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, path);
        }
    }

    [Fact]
    public async Task Instrument_lookup_is_case_insensitive_and_unknown_symbols_are_404()
    {
        var nvda = await _client.GetFromJsonAsync<JsonElement>("/api/v1/instruments/nvda");
        nvda.GetProperty("symbol").GetString().ShouldBe("NVDA");
        nvda.GetProperty("exchange").GetString().ShouldBe("XNAS");
        nvda.GetProperty("name").GetString().ShouldBe("NVIDIA Corp.");
        nvda.GetProperty("marketCap").GetString().ShouldBe("4550000000000.00");

        var missing = await _client.GetAsync("/api/v1/instruments/NOPE");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("instrument_not_found");
    }

    [Fact]
    public async Task Quote_matches_the_design_with_decimal_strings_and_freshness()
    {
        var quote = await _client.GetFromJsonAsync<JsonElement>("/api/v1/market/NVDA/quote");

        quote.GetProperty("last").GetString().ShouldBe("186.40");
        quote.GetProperty("change").GetString().ShouldBe("6.16");
        quote.GetProperty("changePct").GetString().ShouldBe("0.0342");
        quote.GetProperty("prevClose").GetString().ShouldBe("180.24");
        (quote.GetProperty("open").GetString(), quote.GetProperty("high").GetString(), quote.GetProperty("low").GetString()).ShouldBe(("180.60", "186.77", "180.05"));
        quote.GetProperty("volume").GetInt64().ShouldBe(412_800_000);
        quote.GetProperty("quality").GetString().ShouldBe("synthetic");
        quote.GetProperty("source").GetString().ShouldBe("fake");
        quote.GetProperty("asOf").GetString()!.ShouldEndWith("Z");
        quote.GetProperty("stale").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Daily_bars_end_with_the_current_session_and_use_cent_prices()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/v1/market/NVDA/bars?timeframe=3M");

        body.GetProperty("interval").GetString().ShouldBe("1d");
        body.GetProperty("adjusted").GetBoolean().ShouldBeFalse();
        body.GetProperty("levels").ValueKind.ShouldBe(JsonValueKind.Null); // no signals until M3
        var bars = body.GetProperty("bars").EnumerateArray().ToList();
        bars.Count.ShouldBeInRange(55, 70);
        bars[^1].GetProperty("c").GetString().ShouldBe("186.40");
        bars[^2].GetProperty("c").GetString().ShouldBe("180.24");
        bars[^2].TryGetProperty("final", out _).ShouldBeFalse(); // only the forming bar carries "final": false
        bars.Select(b => b.GetProperty("d").GetString()).ShouldBeInOrder();
        foreach (var bar in bars)
        {
            foreach (var field in new[] { "o", "h", "l", "c" })
            {
                bar.GetProperty(field).GetString()!.ShouldMatch(@"^\d+\.\d{2}$");
            }
        }
    }

    [Fact]
    public async Task Intraday_bars_come_from_the_provider()
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)).ToString("yyyy-MM-dd");
        var body = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/market/NVDA/bars?interval=1h&from={from}");

        body.GetProperty("interval").GetString().ShouldBe("1h");
        body.GetProperty("bars").GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Theory]
    [InlineData("timeframe=2W")]
    [InlineData("interval=2h")]
    [InlineData("timeframe=3M&limit=5000")]
    [InlineData("interval=1d&from=2026-11-09&to=2026-11-01")]
    [InlineData("timeframe=3M&interval=1d")]
    public async Task Invalid_bar_requests_are_rejected(string query)
    {
        var response = await _client.GetAsync($"/api/v1/market/NVDA/bars?{query}");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity); // §11.4 M6 errors: 422
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("validation_failed");
    }

    [Fact]
    public async Task Limit_keeps_the_most_recent_bars()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/v1/market/NVDA/bars?timeframe=1Y&limit=10");

        var bars = body.GetProperty("bars").EnumerateArray().ToList();
        bars.Count.ShouldBe(10);
        bars[^1].GetProperty("c").GetString().ShouldBe("186.40");
    }

    [Fact]
    public async Task Fundamentals_are_trimmed_and_nullable()
    {
        var nvda = await _client.GetFromJsonAsync<JsonElement>("/api/v1/instruments/NVDA/fundamentals");
        nvda.GetProperty("beta").GetString().ShouldBe("1.72");
        nvda.GetProperty("peRatio").GetString().ShouldBe("48.6");
        nvda.GetProperty("sector").GetString().ShouldBe("Information Technology");

        var etf = await _client.GetFromJsonAsync<JsonElement>("/api/v1/instruments/SPY/fundamentals");
        etf.GetProperty("beta").ValueKind.ShouldBe(JsonValueKind.Null);
        etf.GetProperty("asOf").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task News_is_fetched_on_demand_and_limited()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/v1/instruments/NVDA/news?limit=3");

        var items = body.GetProperty("items").EnumerateArray().ToList();
        items.Count.ShouldBe(3);
        items.Select(i => i.GetProperty("publishedAt").GetDateTimeOffset()).ShouldBeInOrder(SortDirection.Descending);
        (await _client.GetAsync("/api/v1/instruments/NVDA/news?limit=21")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Breadth_is_derived_from_the_universe()
    {
        var breadth = await _client.GetFromJsonAsync<JsonElement>("/api/v1/market/breadth");

        var advancers = breadth.GetProperty("advancers").GetInt32();
        var decliners = breadth.GetProperty("decliners").GetInt32();
        (advancers + decliners).ShouldBeInRange(900, 1034);
        advancers.ShouldBeGreaterThan(0);
        decliners.ShouldBeGreaterThan(0);
        decimal.Parse(breadth.GetProperty("pctAbove50dma").GetString()!, System.Globalization.CultureInfo.InvariantCulture).ShouldBeInRange(0m, 1m);
        breadth.GetProperty("sentiment").GetString().ShouldBeOneOf("risk_on", "neutral", "risk_off");
    }

    [Fact]
    public async Task Data_that_is_not_stored_is_503_while_the_provider_is_down()
    {
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
            await db.Quotes.Where(q => db.Instruments.Any(i => i.Id == q.InstrumentId && i.Symbol == "ZZ900")).ExecuteDeleteAsync();
        }

        var faults = factory.Services.GetRequiredService<MarketDataFaults>();
        faults.Set(MarketDataFaultMode.Down);
        try
        {
            var quote = await _client.GetAsync("/api/v1/market/ZZ900/quote");
            quote.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            (await quote.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("market_data_unavailable");
            (await _client.GetAsync("/api/v1/market/NVDA/bars?interval=1h")).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

            // Stored data still answers; staleness is the client's cue.
            (await _client.GetAsync("/api/v1/market/NVDA/quote")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            faults.Set(MarketDataFaultMode.Ok);
        }

        (await _client.GetAsync("/api/v1/market/ZZ900/quote")).StatusCode.ShouldBe(HttpStatusCode.OK); // live from the provider again
    }
}
