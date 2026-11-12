using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astryx.Modules.MarketData.Infrastructure.Persistence;
using Astryx.Modules.Quant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.IntegrationTests;

/// <summary>M7 indicator snapshots and their cache (SYSTEM_DESIGN.md §11.4, §13.5, T-061).</summary>
public class IndicatorSnapshotTests(SyncedMarketApiFactory factory) : IClassFixture<SyncedMarketApiFactory>, IAsyncLifetime
{
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await factory.EnsureSyncedAsync();
        _client = await TestUsers.SignedInClientAsync(factory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Nvda_snapshot_has_the_example_shape_and_the_design_values()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/v1/market/NVDA/indicators?interval=1d");
        body.GetProperty("symbol").GetString().ShouldBe("NVDA");
        body.GetProperty("interval").GetString().ShouldBe("1d");
        body.GetProperty("barCount").GetInt32().ShouldBe(300);
        body.GetProperty("insufficient").GetArrayLength().ShouldBe(0);
        var values = body.GetProperty("values");
        foreach (var name in new[] { "sma20", "sma50", "ema20", "macd", "macdSignal", "macdHist", "atr14", "momentum20Pct", "changePct1d", "volatility20Ann" })
        {
            values.GetProperty(name).GetString()!.ShouldMatch(@"^-?\d+\.\d{4}$", name);
        }

        values.GetProperty("rsi14").GetString()!.ShouldMatch(@"^\d+\.\d$");
        values.GetProperty("relVolume20").GetString()!.ShouldMatch(@"^\d+\.\d{2}$");
        values.GetProperty("changePct1d").GetString().ShouldBe("0.0342"); // 186.40 vs 180.24 (design 04)
        int.Parse(values.GetProperty("relStrength").GetString()!).ShouldBeInRange(1, 99);
        values.GetProperty("trend").GetString().ShouldBe("uptrend"); // the breakout fixture climbs for a year
        body.GetProperty("dataQuality").GetString().ShouldBe("ok");
    }

    [Fact]
    public async Task A_second_read_comes_from_the_cache_and_keeps_one_row_per_bar()
    {
        var first = await _client.GetFromJsonAsync<JsonElement>("/api/v1/market/AMD/indicators");
        var second = await _client.GetFromJsonAsync<JsonElement>("/api/v1/market/AMD/indicators");

        second.GetProperty("values").ToString().ShouldBe(first.GetProperty("values").ToString());
        await using var scope = factory.Services.CreateAsyncScope();
        var market = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var amd = await market.Instruments.SingleAsync(i => i.Symbol == "AMD");
        var rows = await scope.ServiceProvider.GetRequiredService<QuantDbContext>().IndicatorSnapshots.Where(r => r.InstrumentId == amd.Id).ToListAsync();
        rows.Count.ShouldBe(1);
        rows[0].DataQuality.ShouldBe(first.GetProperty("dataQuality").GetString());
        rows[0].ParamsHash.Length.ShouldBe(16);
    }

    [Fact]
    public async Task Short_history_is_422_insufficient_history_and_unknown_symbols_404()
    {
        // ZZ940 keeps only its latest 20 daily bars: one short of the minimum.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var market = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
            var zz = await market.Instruments.SingleAsync(i => i.Symbol == "ZZ940");
            await market.Database.ExecuteSqlAsync($"""
                DELETE FROM market.price_bars WHERE instrument_id = {zz.Id} AND timeframe = '1d' AND bar_start < (
                    SELECT bar_start FROM market.price_bars WHERE instrument_id = {zz.Id} AND timeframe = '1d' ORDER BY bar_start DESC OFFSET 19 LIMIT 1)
                """);
            await market.Database.ExecuteSqlAsync($"DELETE FROM market.quotes WHERE instrument_id = {zz.Id}");
        }

        var shortHistory = await _client.GetAsync("/api/v1/market/ZZ940/indicators");
        shortHistory.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await shortHistory.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("insufficient_history");

        var unknown = await _client.GetAsync("/api/v1/market/NOPE/indicators");
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await unknown.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("instrument_not_found");
    }

    [Fact]
    public async Task Only_daily_interval_and_a_session_are_accepted()
    {
        (await _client.GetAsync("/api/v1/market/NVDA/indicators?interval=5m")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await factory.CreateClient().GetAsync("/api/v1/market/NVDA/indicators")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
