using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astryx.Modules.Paper.Infrastructure.Persistence;
using Astryx.Modules.Portfolio.Application;
using Astryx.Modules.Portfolio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.IntegrationTests;

/// <summary>T9–T11 (SYSTEM_DESIGN.md §11.7, §15.5, PAPER-07, TASKS.md T-086). 64 NVDA filled at 186.62.</summary>
public class ClosePositionTests(TradingApiFactory factory) : IClassFixture<TradingApiFactory>, IAsyncLifetime
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
    public async Task PAPER_07_closing_realizes_the_pnl_credits_cash_and_cancels_the_exits()
    {
        var (client, order) = await PlaceAsync(186.60m);
        var positionId = order.GetProperty("positionId").GetGuid();
        await factory.MakeQuoteLiveAsync("NVDA", 190.00m);

        var preview = await (await client.PostAsync($"/api/v1/positions/{positionId}/close-preview", null)).Content.ReadFromJsonAsync<JsonElement>();
        (preview.GetProperty("quantity").GetString(), preview.GetProperty("marketValue").GetString(), preview.GetProperty("estExitPrice").GetString(), preview.GetProperty("estRealizedPnl").GetString())
            .ShouldBe(("64", "12160.00", "189.98", "215.04")); // 64 × (189.98 − 186.62)

        var response = await SendAsync(client, $"/api/v1/positions/{positionId}/close");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var position = body.GetProperty("position");
        (position.GetProperty("status").GetString(), position.GetProperty("exitReason").GetString(), position.GetProperty("exitPrice").GetString(), position.GetProperty("realizedPnl").GetString())
            .ShouldBe(("closed", "manual", "189.98", "215.04"));
        (body.GetProperty("order").GetProperty("purpose").GetString(), body.GetProperty("order").GetProperty("status").GetString()).ShouldBe(("exit_manual", "filled"));
        var portfolioId = await Query<PaperDbContext, Guid>(db => db.Orders.IgnoreQueryFilters().Where(o => o.PositionId == positionId).Select(o => o.PortfolioId).FirstAsync());
        (await Query<PortfolioDbContext, decimal>(db => db.Portfolios.IgnoreQueryFilters().Where(p => p.Id == portfolioId).Select(p => p.CashBalance).SingleAsync()))
            .ShouldBe(100_000m - (64 * 186.62m) + (64 * 189.98m));
        (await Query<PortfolioDbContext, decimal>(db => db.Portfolios.IgnoreQueryFilters().Where(p => p.Id == portfolioId).Select(p => p.RealizedPnl).SingleAsync())).ShouldBe(215.04m);
        (await Query<PaperDbContext, List<string>>(db => db.Orders.IgnoreQueryFilters().Where(o => o.PositionId == positionId && o.OcoGroupId != null).Select(o => o.Status).ToListAsync()))
            .ShouldAllBe(s => s == "cancelled");
        (await Query<PortfolioDbContext, IReadOnlyList<string>>(db => LedgerInvariants.CheckAsync(db, portfolioId))).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_closed_position_cannot_be_closed_again_but_the_same_key_replays()
    {
        var (client, order) = await PlaceAsync(186.60m);
        var positionId = order.GetProperty("positionId").GetGuid();
        var key = $"close-{Guid.NewGuid():N}";
        var first = await SendAsync(client, $"/api/v1/positions/{positionId}/close", key: key);

        var replay = await SendAsync(client, $"/api/v1/positions/{positionId}/close", key: key);
        var again = await SendAsync(client, $"/api/v1/positions/{positionId}/close");

        (await replay.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("position_not_open");
        (await client.PostAsync($"/api/v1/positions/{positionId}/close-preview", null)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Cancelling_a_working_entry_releases_its_cash_and_filled_orders_cannot_be_cancelled()
    {
        var (client, working) = await PlaceAsync(186.40m);
        var orderId = working.GetProperty("id").GetGuid();

        var cancelled = await SendAsync(client, $"/api/v1/paper/orders/{orderId}/cancel");

        cancelled.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await cancelled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().ShouldBe("cancelled");
        var portfolioId = await Query<PaperDbContext, Guid>(db => db.Orders.IgnoreQueryFilters().Where(o => o.Id == orderId).Select(o => o.PortfolioId).SingleAsync());
        (await Query<PortfolioDbContext, decimal>(db => db.Portfolios.IgnoreQueryFilters().Where(p => p.Id == portfolioId).Select(p => p.ReservedCash).SingleAsync())).ShouldBe(0m);
        (await SendAsync(client, $"/api/v1/paper/orders/{orderId}/cancel")).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var (other, filled) = await PlaceAsync(186.60m);
        (await SendAsync(other, $"/api/v1/paper/orders/{filled.GetProperty("id").GetGuid()}/cancel")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Another_users_position_and_order_are_not_found()
    {
        var (_, order) = await PlaceAsync(186.60m);
        var bob = await TestUsers.SignedInClientAsync(factory);
        var positionId = order.GetProperty("positionId").GetGuid();

        (await bob.PostAsync($"/api/v1/positions/{positionId}/close-preview", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(bob, $"/api/v1/positions/{positionId}/close")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(bob, $"/api/v1/paper/orders/{order.GetProperty("id").GetGuid()}/cancel")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
