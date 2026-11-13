using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astryx.Infrastructure.Outbox;
using Astryx.Modules.Paper.Infrastructure.Persistence;
using Astryx.Modules.Portfolio.Application;
using Astryx.Modules.Portfolio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.IntegrationTests;

/// <summary>P11 paper account reset (SYSTEM_DESIGN.md §11.8, PAPER-08, TASKS.md T-099). 64 NVDA fill at 186.62 by default.</summary>
public class PaperResetTests(TradingApiFactory factory) : IClassFixture<TradingApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await factory.PrepareAsync();
        await factory.ScanAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(HttpClient Client, JsonElement Order)> PlaceAsync(decimal last)
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        await factory.MakeQuoteLiveAsync("NVDA", last);
        var proposal = await (await client.PostAsJsonAsync("/api/v1/proposals", new { source = "signal", signalId = await factory.ActiveSignalIdAsync("NVDA") })).Content.ReadFromJsonAsync<JsonElement>();
        var response = await SendAsync(client, "/api/v1/paper/orders", new { proposalId = proposal.GetProperty("id").GetGuid(), assessmentId = proposal.GetProperty("assessment").GetProperty("id").GetGuid() });
        return (client, await response.Content.ReadFromJsonAsync<JsonElement>());
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string path, object? body = null, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body ?? new { }) };
        request.Headers.Add("Idempotency-Key", key ?? $"key-{Guid.NewGuid():N}");
        return client.SendAsync(request);
    }

    private async Task<T> Query<TContext, T>(Func<TContext, Task<T>> query)
        where TContext : notnull
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<TContext>());
    }

    [Fact]
    public async Task PAPER_08_reset_closes_at_the_last_price_cancels_orders_restores_cash_and_starts_a_new_epoch()
    {
        var (client, order) = await PlaceAsync(186.60m);
        var positionId = order.GetProperty("positionId").GetGuid();
        await factory.MakeQuoteLiveAsync("NVDA", 190.00m);
        factory.Session.CloseIn = null; // no session today: a reset doesn't need the market
        try
        {
            var response = await SendAsync(client, "/api/v1/portfolio/reset", new { confirm = "RESET" });

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            (body.GetProperty("closedPositions").GetInt32(), body.GetProperty("cancelledOrders").GetInt32(), body.GetProperty("resetCount").GetInt32()).ShouldBe((1, 2, 1)); // the stop and target legs
            decimal.Parse(body.GetProperty("cashBalance").GetString()!).ShouldBe(100_000m);
        }
        finally
        {
            factory.Session.CloseIn = TimeSpan.FromHours(3);
        }

        var portfolioId = await Query<PaperDbContext, Guid>(db => db.Orders.IgnoreQueryFilters().Where(o => o.PositionId == positionId).Select(o => o.PortfolioId).FirstAsync());
        var position = await Query<PortfolioDbContext, Astryx.Modules.Portfolio.Domain.Position>(db => db.Positions.IgnoreQueryFilters().SingleAsync(p => p.Id == positionId));
        (position.Status, position.ExitReason, position.ExitPrice).ShouldBe(("closed", "reset", 190.00m)); // last price, no slippage
        var portfolio = await Query<PortfolioDbContext, Astryx.Modules.Portfolio.Domain.Portfolio>(db => db.Portfolios.IgnoreQueryFilters().SingleAsync(p => p.Id == portfolioId));
        (portfolio.CashBalance, portfolio.ReservedCash, portfolio.RealizedPnl, portfolio.StartOfDayEquity, portfolio.ResetCount).ShouldBe((100_000m, 0m, 0m, 100_000m, 1));
        (await Query<PaperDbContext, List<string>>(db => db.Orders.IgnoreQueryFilters().Where(o => o.PositionId == positionId && o.OcoGroupId != null).Select(o => o.Status).ToListAsync()))
            .ShouldAllBe(s => s == "cancelled");
        var ledger = await Query<PortfolioDbContext, List<(string Type, decimal Amount)>>(async db =>
            (await db.Transactions.IgnoreQueryFilters().Where(t => t.PortfolioId == portfolioId).OrderBy(t => t.Seq).Select(t => new { t.Type, t.Amount }).ToListAsync()).Select(t => (t.Type, t.Amount)).ToList());
        ledger.Select(t => t.Type).ShouldBe(["initial_deposit", "buy", "reset_close", "reset_deposit"]);
        ledger[2].Amount.ShouldBe(64 * 190.00m);
        ledger[3].Amount.ShouldBe(100_000m - (100_000m - (64 * 186.62m) + (64 * 190.00m))); // the gain is taken back out
        (await Query<PortfolioDbContext, IReadOnlyList<string>>(db => LedgerInvariants.CheckAsync(db, portfolioId))).ShouldBeEmpty();

        // The new epoch: no trades, all-time P&L back to zero, and the reset is in Activity.
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/portfolio/trades")).GetProperty("items").GetArrayLength().ShouldBe(0);
        var summary = await client.GetFromJsonAsync<JsonElement>("/api/v1/portfolio");
        (summary.GetProperty("equity").GetString(), summary.GetProperty("allTimePnl").GetString(), summary.GetProperty("openPositions").GetInt32()).ShouldBe(("100000.00", "0.00", 0));
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/portfolio/performance?range=ALL")).GetProperty("metrics").GetProperty("trades").GetInt32().ShouldBe(0);
        var dispatcher = factory.Services.GetRequiredService<OutboxDispatcher>();
        while (await dispatcher.DispatchOnceAsync(default) > 0)
        {
        }

        var activity = (await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications?category=system")).GetProperty("items").EnumerateArray().ToList();
        activity.ShouldContain(i => i.GetProperty("type").GetString() == "paper_reset" && i.GetProperty("body").GetString()!.StartsWith("Closed 1 position and cancelled 2 orders."));
    }

    [Fact]
    public async Task A_working_entry_is_cancelled_and_its_reservation_released()
    {
        var (client, order) = await PlaceAsync(185.00m); // below the 186.50 buy stop: working
        order.GetProperty("status").GetString().ShouldBe("working");

        var response = await SendAsync(client, "/api/v1/portfolio/reset", new { confirm = "RESET" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var orderId = order.GetProperty("id").GetGuid();
        var after = await Query<PaperDbContext, Astryx.Modules.Paper.Domain.Order>(db => db.Orders.IgnoreQueryFilters().SingleAsync(o => o.Id == orderId));
        (after.Status, after.ReservedAmount).ShouldBe(("cancelled", 0m));
        var portfolio = await Query<PortfolioDbContext, Astryx.Modules.Portfolio.Domain.Portfolio>(db => db.Portfolios.IgnoreQueryFilters().SingleAsync(p => p.Id == after.PortfolioId));
        (portfolio.CashBalance, portfolio.ReservedCash).ShouldBe((100_000m, 0m));
        // Already at the starting cash: no reset_deposit is needed.
        (await Query<PortfolioDbContext, int>(db => db.Transactions.IgnoreQueryFilters().CountAsync(t => t.PortfolioId == after.PortfolioId && t.Type == "reset_deposit"))).ShouldBe(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("reset")]
    [InlineData("YES")]
    public async Task The_RESET_confirmation_is_required(string? confirm)
    {
        var client = await TestUsers.SignedInClientAsync(factory);

        var response = await SendAsync(client, "/api/v1/portfolio/reset", new { confirm });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_replayed_key_resets_once()
    {
        var (client, _) = await PlaceAsync(186.60m);
        var key = $"reset-{Guid.NewGuid():N}";

        var first = await SendAsync(client, "/api/v1/portfolio/reset", new { confirm = "RESET" }, key);
        var second = await SendAsync(client, "/api/v1/portfolio/reset", new { confirm = "RESET" }, key);

        (first.StatusCode, second.StatusCode).ShouldBe((HttpStatusCode.OK, HttpStatusCode.OK));
        (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resetCount").GetInt32().ShouldBe(1);
        (await SendAsync(client, "/api/v1/portfolio/reset", new { confirm = "RESET" }) is { StatusCode: HttpStatusCode.OK } third
            ? (await third.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resetCount").GetInt32() : 0).ShouldBe(2);
        (await client.PostAsJsonAsync("/api/v1/portfolio/reset", new { confirm = "RESET" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest); // key required
    }
}
