using System.Net.Http.Json;
using System.Text.Json;
using Astryx.Contracts.Events.MarketData;
using Astryx.Infrastructure.Jobs;
using Astryx.Modules.MarketData.Infrastructure.Persistence;
using Astryx.Modules.Paper.Application;
using Astryx.Modules.Paper.Infrastructure.Persistence;
using Astryx.Modules.Portfolio.Application;
using Astryx.Modules.Portfolio.Domain;
using Astryx.Modules.Portfolio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.IntegrationTests;

/// <summary>Valuation, snapshots, performance and trades (SYSTEM_DESIGN.md §11.8 P1/P2/P5/P6, TASKS.md T-090).</summary>
public class PortfolioReadTests(TradingApiFactory factory) : IClassFixture<TradingApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await factory.PrepareAsync();
        await factory.ScanAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>A new user holding 64 NVDA filled at 186.62, with NVDA last at 186.60.</summary>
    private async Task<(HttpClient Client, Guid PortfolioId, Guid PositionId)> HoldNvdaAsync()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        await factory.MakeQuoteLiveAsync("NVDA", 186.60m);
        var proposal = await (await client.PostAsJsonAsync("/api/v1/proposals", new { source = "signal", signalId = await factory.ActiveSignalIdAsync("NVDA") })).Content.ReadFromJsonAsync<JsonElement>();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/paper/orders")
        {
            Content = JsonContent.Create(new { proposalId = proposal.GetProperty("id").GetGuid(), assessmentId = proposal.GetProperty("assessment").GetProperty("id").GetGuid() }),
        };
        request.Headers.Add("Idempotency-Key", $"order-{Guid.NewGuid():N}");
        var order = await (await client.SendAsync(request)).Content.ReadFromJsonAsync<JsonElement>();
        var positionId = order.GetProperty("positionId").GetGuid();
        await using var scope = factory.Services.CreateAsyncScope();
        var portfolioId = await scope.ServiceProvider.GetRequiredService<PaperDbContext>().Orders.IgnoreQueryFilters().Where(o => o.PositionId == positionId).Select(o => o.PortfolioId).FirstAsync();
        return (client, portfolioId, positionId);
    }

    [Fact]
    public async Task The_portfolio_is_valued_at_the_latest_quotes()
    {
        var (client, _, _) = await HoldNvdaAsync();

        var p = await client.GetFromJsonAsync<JsonElement>("/api/v1/portfolio");

        (p.GetProperty("mode").GetString(), p.GetProperty("cash").GetString(), p.GetProperty("invested").GetString(), p.GetProperty("equity").GetString())
            .ShouldBe(("paper", "88056.32", "11942.40", "99998.72"));
        (p.GetProperty("unrealizedPnl").GetString(), p.GetProperty("allTimePnl").GetString(), p.GetProperty("allTimeReturnPct").GetString(), p.GetProperty("openPositions").GetInt32())
            .ShouldBe(("-1.28", "-1.28", "0.0000", 1));
        p.GetProperty("halted").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Positions_carry_weight_against_the_cap_stop_distance_and_the_new_badge()
    {
        var (client, _, positionId) = await HoldNvdaAsync();

        var row = (await client.GetFromJsonAsync<JsonElement>("/api/v1/portfolio/positions")).GetProperty("items").EnumerateArray().Single();

        row.GetProperty("id").GetGuid().ShouldBe(positionId);
        (row.GetProperty("quantity").GetString(), row.GetProperty("avgEntryPrice").GetString(), row.GetProperty("marketValue").GetString(), row.GetProperty("weightPct").GetString(), row.GetProperty("weightCapPct").GetString())
            .ShouldBe(("64", "186.62", "11942.40", "0.1194", "0.1200"));
        (row.GetProperty("stopDistancePct").GetString(), row.GetProperty("nearStop").GetBoolean(), row.GetProperty("isNew").GetBoolean(), row.GetProperty("overCap").GetBoolean())
            .ShouldBe(("0.0450", false, true, false));
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/portfolio/positions?status=closed")).GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Snapshots_are_written_once_per_slot_and_feed_performance_and_trades()
    {
        var (client, portfolioId, _) = await HoldNvdaAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<PortfolioSnapshotHandler>();
            await handler.HandleAsync(new PortfolioSnapshotJob(), new JobContext(Guid.NewGuid(), 1, 1, null), default);
            await handler.HandleAsync(new PortfolioSnapshotJob(), new JobContext(Guid.NewGuid(), 1, 1, null), default);
            (await scope.ServiceProvider.GetRequiredService<PortfolioDbContext>().Snapshots.IgnoreQueryFilters().CountAsync(s => s.PortfolioId == portfolioId && s.Kind == "hourly")).ShouldBe(1);

            var db = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
            db.Snapshots.Add(new PortfolioSnapshot { PortfolioId = portfolioId, Kind = "eod", SnapshotAt = DateTimeOffset.UtcNow.AddDays(-2), Equity = 100_000m, Cash = 100_000m, BenchmarkClose = 500m });
            db.Snapshots.Add(new PortfolioSnapshot { PortfolioId = portfolioId, Kind = "eod", SnapshotAt = DateTimeOffset.UtcNow.AddDays(-1), Equity = 101_000m, Cash = 88_056.32m, BenchmarkClose = 505m });
            await db.SaveChangesAsync();
        }

        // Close at the target, so there is one closed trade.
        await factory.MakeQuoteLiveAsync("NVDA", 204.10m);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var instrument = await scope.ServiceProvider.GetRequiredService<MarketDataDbContext>().Instruments.Where(i => i.Symbol == "NVDA").Select(i => i.Id).SingleAsync();
            await scope.ServiceProvider.GetRequiredService<PositionMonitor>().EvaluateAsync(new QuoteChange(instrument, "NVDA", 204.10m, DateTimeOffset.UtcNow, 0), default);
        }

        var perf = await client.GetFromJsonAsync<JsonElement>("/api/v1/portfolio/performance?range=1M");
        perf.GetProperty("benchmark").GetString().ShouldBe("SPY");
        var series = perf.GetProperty("series").EnumerateArray().ToList();
        series.Count.ShouldBe(3); // two EOD snapshots and the live point
        (series[0].GetProperty("equity").GetString(), series[0].GetProperty("benchmark").GetString()).ShouldBe(("100000.00", "100000.00"));
        series[^1].GetProperty("equity").GetString().ShouldBe("101112.32"); // 100,000 + 64 × (204.00 − 186.62)
        var metrics = perf.GetProperty("metrics");
        (metrics.GetProperty("twrPct").GetString(), metrics.GetProperty("trades").GetInt32(), metrics.GetProperty("winRate").GetString(), metrics.GetProperty("avgWin").GetString())
            .ShouldBe(("0.0111", 1, "1.00", "1112.32"));
        (await client.GetAsync("/api/v1/portfolio/performance?range=5Y")).StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest);

        var trade = (await client.GetFromJsonAsync<JsonElement>("/api/v1/portfolio/trades")).GetProperty("items").EnumerateArray().Single();
        (trade.GetProperty("exitReason").GetString(), trade.GetProperty("exitPrice").GetString(), trade.GetProperty("realizedPnl").GetString(), trade.GetProperty("returnPct").GetString())
            .ShouldBe(("target", "204.00", "1112.32", "0.0931"));
    }
}
