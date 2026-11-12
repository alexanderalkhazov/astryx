using System.Net.Http.Json;
using System.Text.Json;
using Astryx.Contracts.Events.MarketData;
using Astryx.Infrastructure.Jobs;
using Astryx.Infrastructure.Persistence;
using Astryx.Modules.MarketData.Infrastructure.Persistence;
using Astryx.Modules.Paper.Application;
using Astryx.Modules.Paper.Infrastructure.Persistence;
using Astryx.Modules.Portfolio.Application;
using Astryx.Modules.Portfolio.Infrastructure.Persistence;
using Astryx.Modules.Trading.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.IntegrationTests;

/// <summary>The position monitor and session jobs (SYSTEM_DESIGN.md §15.4, PAPER-02…06, TASKS.md T-085). Default NVDA size: 64 shares.</summary>
public class PositionMonitorTests(TradingApiFactory factory) : IClassFixture<TradingApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await factory.PrepareAsync();
        await factory.ScanAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record Placed(Guid PortfolioId, Guid OrderId, Guid ProposalId, Guid? PositionId);

    private async Task<Placed> PlaceAsync(decimal last)
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        await factory.MakeQuoteLiveAsync("NVDA", last);
        var proposal = await (await client.PostAsJsonAsync("/api/v1/proposals", new { source = "signal", signalId = await factory.ActiveSignalIdAsync("NVDA") })).Content.ReadFromJsonAsync<JsonElement>();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/paper/orders")
        {
            Content = JsonContent.Create(new { proposalId = proposal.GetProperty("id").GetGuid(), assessmentId = proposal.GetProperty("assessment").GetProperty("id").GetGuid() }),
        };
        request.Headers.Add("Idempotency-Key", $"order-{Guid.NewGuid():N}");
        var order = await (await client.SendAsync(request)).Content.ReadFromJsonAsync<JsonElement>();
        var orderId = order.GetProperty("id").GetGuid();
        var portfolioId = await Query<PaperDbContext, Guid>(db => db.Orders.IgnoreQueryFilters().Where(o => o.Id == orderId).Select(o => o.PortfolioId).SingleAsync());
        Guid? positionId = order.GetProperty("positionId").ValueKind == JsonValueKind.Null ? null : order.GetProperty("positionId").GetGuid();
        return new Placed(portfolioId, orderId, proposal.GetProperty("id").GetGuid(), positionId);
    }

    private async Task TickAsync(decimal last)
    {
        await factory.MakeQuoteLiveAsync("NVDA", last);
        var instrument = await Query<MarketDataDbContext, Guid>(db => db.Instruments.Where(i => i.Symbol == "NVDA").Select(i => i.Id).SingleAsync());
        await EvaluateAsync(new QuoteChange(instrument, "NVDA", last, DateTimeOffset.UtcNow, 0));
    }

    /// <summary>Outbox payloads of a type that mention an id (jsonb can't be searched with LIKE, so filter here).</summary>
    private async Task<List<string>> OutboxAsync(string type, Guid id) =>
        (await Query<InfraDbContext, List<string>>(db => db.OutboxMessages.Where(m => m.Type == type).Select(m => m.Payload).ToListAsync()))
            .Where(p => p.Contains(id.ToString(), StringComparison.Ordinal)).ToList();

    private async Task EvaluateAsync(QuoteChange quote)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PositionMonitor>().EvaluateAsync(quote, default);
    }

    private async Task<T> Query<TContext, T>(Func<TContext, Task<T>> query)
        where TContext : notnull
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<TContext>());
    }

    private async Task AssertBalancedAsync(Guid portfolioId) =>
        (await Query<PortfolioDbContext, IReadOnlyList<string>>(db => LedgerInvariants.CheckAsync(db, portfolioId))).ShouldBeEmpty();

    [Fact]
    public async Task PAPER_02_a_working_buy_stop_fills_on_the_tick_that_reaches_it()
    {
        var placed = await PlaceAsync(186.40m);
        await TickAsync(186.45m);
        (await Query<PaperDbContext, string>(db => db.Orders.IgnoreQueryFilters().Where(o => o.Id == placed.OrderId).Select(o => o.Status).SingleAsync())).ShouldBe("working");

        await TickAsync(186.55m);

        var order = await Query<PaperDbContext, Astryx.Modules.Paper.Domain.Order>(db => db.Orders.IgnoreQueryFilters().SingleAsync(o => o.Id == placed.OrderId));
        (order.Status, order.AvgFillPrice, order.ReservedAmount).ShouldBe(("filled", 186.57m, 0m));
        var portfolio = await Query<PortfolioDbContext, Astryx.Modules.Portfolio.Domain.Portfolio>(db => db.Portfolios.IgnoreQueryFilters().SingleAsync(p => p.Id == placed.PortfolioId));
        (portfolio.CashBalance, portfolio.ReservedCash).ShouldBe((100_000m - (64 * 186.57m), 0m));
        (await Query<PaperDbContext, int>(db => db.Orders.IgnoreQueryFilters().CountAsync(o => o.PositionId == order.PositionId && o.Status == "working"))).ShouldBe(2); // OCO exits
        await AssertBalancedAsync(placed.PortfolioId);
    }

    [Fact]
    public async Task PAPER_04_a_gap_through_the_stop_closes_below_plan_and_cancels_the_target()
    {
        var placed = await PlaceAsync(186.60m);

        await TickAsync(175.00m);

        var position = await Query<PortfolioDbContext, Astryx.Modules.Portfolio.Domain.Position>(db => db.Positions.IgnoreQueryFilters().SingleAsync(p => p.Id == placed.PositionId));
        (position.Status, position.ExitReason, position.ExitPrice, position.RealizedPnl).ShouldBe(("closed", "stop", 174.98m, 64 * (174.98m - 186.62m)));
        position.RealizedPnl!.Value.ShouldBeLessThan(-(64 * 8.30m)); // worse than the planned max loss
        var exits = await Query<PaperDbContext, List<(string, string)>>(async db => (await db.Orders.IgnoreQueryFilters().Where(o => o.PositionId == placed.PositionId && o.Purpose != "entry").ToListAsync()).Select(o => (o.Purpose, o.Status)).OrderBy(o => o.Purpose).ToList());
        exits.ShouldBe([("exit_stop", "filled"), ("exit_target", "cancelled")]);
        (await Query<PortfolioDbContext, decimal>(db => db.Portfolios.IgnoreQueryFilters().Where(p => p.Id == placed.PortfolioId).Select(p => p.CashBalance).SingleAsync()))
            .ShouldBe(100_000m - (64 * 186.62m) + (64 * 174.98m));
        JsonDocument.Parse((await OutboxAsync("paper.position_closed.v1", placed.PositionId!.Value)).Single()).RootElement.GetProperty("gappedThroughStop").GetBoolean().ShouldBeTrue();
        await AssertBalancedAsync(placed.PortfolioId);
    }

    [Fact]
    public async Task The_target_fills_at_the_target()
    {
        var placed = await PlaceAsync(186.60m);

        await TickAsync(204.10m);

        var position = await Query<PortfolioDbContext, Astryx.Modules.Portfolio.Domain.Position>(db => db.Positions.IgnoreQueryFilters().SingleAsync(p => p.Id == placed.PositionId));
        (position.ExitReason, position.ExitPrice, position.RealizedPnl).ShouldBe(("target", 204.00m, 64 * (204.00m - 186.62m)));
        await AssertBalancedAsync(placed.PortfolioId);
    }

    [Fact]
    public async Task PAPER_06_concurrent_triggers_fill_exactly_one_exit()
    {
        var placed = await PlaceAsync(186.60m);
        await factory.MakeQuoteLiveAsync("NVDA", 175.00m);
        var instrument = await Query<MarketDataDbContext, Guid>(db => db.Instruments.Where(i => i.Symbol == "NVDA").Select(i => i.Id).SingleAsync());
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => EvaluateAsync(new QuoteChange(instrument, "NVDA", 175.00m, DateTimeOffset.UtcNow, 0))));

        (await Query<PaperDbContext, int>(db => db.Fills.IgnoreQueryFilters().CountAsync(f => f.Side == "sell" && db.Orders.Any(o => o.Id == f.OrderId && o.PositionId == placed.PositionId)))).ShouldBe(1);
        (await Query<PortfolioDbContext, int>(db => db.Transactions.IgnoreQueryFilters().CountAsync(t => t.PortfolioId == placed.PortfolioId && t.Type == "sell"))).ShouldBe(1);
        await AssertBalancedAsync(placed.PortfolioId);
    }

    [Fact]
    public async Task A_stop_proximity_alert_fires_once_per_session()
    {
        var placed = await PlaceAsync(186.60m);

        await TickAsync(180.00m);
        await TickAsync(179.50m);

        (await OutboxAsync("paper.stop_proximity.v1", placed.PositionId!.Value)).Count.ShouldBe(1);
        (await Query<PortfolioDbContext, string>(db => db.Positions.IgnoreQueryFilters().Where(p => p.Id == placed.PositionId).Select(p => p.Status).SingleAsync())).ShouldBe("open");
    }

    [Fact]
    public async Task PAPER_03_a_working_day_order_expires_at_the_close_and_releases_its_cash()
    {
        var placed = await PlaceAsync(186.40m);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PaperDbContext>().Orders.IgnoreQueryFilters().Where(o => o.Id == placed.OrderId)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
            await scope.ServiceProvider.GetRequiredService<DayOrderExpiryHandler>().HandleAsync(new DayOrderExpiryJob(), new JobContext(Guid.NewGuid(), 1, 1, null), default);
        }

        (await Query<PaperDbContext, string>(db => db.Orders.IgnoreQueryFilters().Where(o => o.Id == placed.OrderId).Select(o => o.Status).SingleAsync())).ShouldBe("expired");
        (await Query<PortfolioDbContext, decimal>(db => db.Portfolios.IgnoreQueryFilters().Where(p => p.Id == placed.PortfolioId).Select(p => p.ReservedCash).SingleAsync())).ShouldBe(0m);
        (await Query<TradingDbContext, string>(db => db.Proposals.IgnoreQueryFilters().Where(p => p.Id == placed.ProposalId).Select(p => p.Status).SingleAsync())).ShouldBe("approved");
        await AssertBalancedAsync(placed.PortfolioId);
    }

    [Fact]
    public async Task Start_of_day_stamps_equity_at_the_open()
    {
        var placed = await PlaceAsync(186.60m);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PortfolioDbContext>().Portfolios.IgnoreQueryFilters().Where(p => p.Id == placed.PortfolioId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.StartOfDayDate, (DateOnly?)null));
            await scope.ServiceProvider.GetRequiredService<StartOfDayHandler>().HandleAsync(new StartOfDayJob(), new JobContext(Guid.NewGuid(), 1, 1, null), default);
        }

        var portfolio = await Query<PortfolioDbContext, Astryx.Modules.Portfolio.Domain.Portfolio>(db => db.Portfolios.IgnoreQueryFilters().SingleAsync(p => p.Id == placed.PortfolioId));
        portfolio.StartOfDayEquity.ShouldBe(100_000m - (64 * 186.62m) + (64 * 186.60m)); // cash + 64 NVDA at the last quote
        portfolio.StartOfDayDate.ShouldNotBeNull();
    }
}
