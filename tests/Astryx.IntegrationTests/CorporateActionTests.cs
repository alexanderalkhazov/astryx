using System.Net.Http.Json;
using System.Text.Json;
using Astryx.Contracts.Events;
using Astryx.Contracts.Events.MarketData;
using Astryx.Contracts.MarketData;
using Astryx.Modules.MarketData.Domain;
using Astryx.Modules.MarketData.Infrastructure.Persistence;
using Astryx.Modules.Quant.Application.Analysis;
using Astryx.Modules.Quant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.IntegrationTests;

/// <summary>Corporate-action adjustment over unadjusted storage (SYSTEM_DESIGN.md §20.7, TASKS.md T-044). Its own database: it adds actions to NVDA.</summary>
public class CorporateActionTests(SyncedMarketApiFactory factory) : IClassFixture<SyncedMarketApiFactory>, IAsyncLifetime
{
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await factory.EnsureSyncedAsync();
        _client = await TestUsers.SignedInClientAsync(factory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(Guid InstrumentId, List<(DateOnly D, decimal Close, long Volume)> Raw)> RawBarsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var id = await db.Instruments.Where(i => i.Symbol == "NVDA").Select(i => i.Id).SingleAsync();
        var raw = (await db.PriceBars.AsNoTracking().Where(b => b.InstrumentId == id && b.Timeframe == Timeframe.OneDay && b.IsFinal)
                .OrderBy(b => b.SessionDate).Select(b => new { b.SessionDate, b.Close, b.Volume }).ToListAsync())
            .Select(b => (b.SessionDate, b.Close, b.Volume)).ToList();
        return (id, raw);
    }

    private async Task AddActionAsync(Guid instrumentId, string type, DateOnly exDate, decimal? ratio, decimal? cash)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        db.CorporateActions.Add(new CorporateAction { Id = Guid.NewGuid(), InstrumentId = instrumentId, ActionType = type, ExDate = exDate, Ratio = ratio, CashAmount = cash, Source = "test" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_4_for_1_split_and_a_dividend_adjust_history_on_read_without_touching_storage()
    {
        var (id, raw) = await RawBarsAsync();
        var ex = raw[^5].D; // four sessions before the latest final bar
        await AddActionAsync(id, CorporateActionType.Split, ex, 4m, null);

        var body = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/market/NVDA/bars?interval=1d&from={raw[0].D:yyyy-MM-dd}&to={raw[^1].D:yyyy-MM-dd}");

        body.GetProperty("adjusted").GetBoolean().ShouldBeTrue();
        var bars = body.GetProperty("bars").EnumerateArray().Where(b => !b.TryGetProperty("final", out _))
            .ToDictionary(b => DateOnly.Parse(b.GetProperty("d").GetString()!), b => (Close: decimal.Parse(b.GetProperty("c").GetString()!), Volume: b.GetProperty("v").GetInt64()));
        var before = raw[^6];
        bars[before.D].Close.ShouldBe(Math.Round(before.Close / 4m, 2, MidpointRounding.AwayFromZero));
        bars[before.D].Volume.ShouldBe(before.Volume * 4);
        bars[ex].Close.ShouldBe(Math.Round(raw[^5].Close, 2, MidpointRounding.AwayFromZero)); // the ex-date itself is post-split already
        (await RawBarsAsync()).Raw.ShouldBe(raw); // storage stays unadjusted

        // Quant reads split-only by default; total return also applies a dividend.
        await AddActionAsync(id, CorporateActionType.CashDividend, raw[^3].D, null, 1.00m);
        await using var scope = factory.Services.CreateAsyncScope();
        var history = scope.ServiceProvider.GetRequiredService<IPriceHistoryProvider>();
        var splitOnly = (await history.GetDailyAsync("NVDA", 400, default))!.Bars.Single(b => b.SessionDate == raw[^4].D);
        var totalReturn = (await history.GetDailyAsync("NVDA", 400, default, AdjustmentMode.TotalReturn))!.Bars.Single(b => b.SessionDate == raw[^4].D);
        splitOnly.Close.ShouldBe(Math.Round(raw[^4].Close, 2, MidpointRounding.AwayFromZero));
        totalReturn.Close.ShouldBe(Math.Round(raw[^4].Close * (1m - (1.00m / raw[^4].Close)), 2, MidpointRounding.AwayFromZero)); // C(ex−1) is that same session
    }

    [Fact]
    public async Task A_recorded_action_drops_the_instruments_cached_indicators()
    {
        (await _client.GetAsync("/api/v1/market/NVDA/indicators?interval=1d")).EnsureSuccessStatusCode(); // computes and caches
        var (id, raw) = await RawBarsAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var quant = scope.ServiceProvider.GetRequiredService<QuantDbContext>();
        (await quant.IndicatorSnapshots.IgnoreQueryFilters().CountAsync(s => s.InstrumentId == id)).ShouldBeGreaterThan(0);

        await scope.ServiceProvider.GetRequiredService<IndicatorCacheInvalidation>()
            .HandleAsync(new CorporateActionRecorded(id, "NVDA", CorporateActionType.Split, raw[^2].D), new IntegrationEventContext(Guid.NewGuid(), DateTimeOffset.UtcNow, null), default);

        (await quant.IndicatorSnapshots.IgnoreQueryFilters().CountAsync(s => s.InstrumentId == id)).ShouldBe(0);
    }
}
