using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astryx.Modules.Paper.Infrastructure.Persistence;
using Astryx.Modules.Portfolio.Application;
using Astryx.Modules.Portfolio.Infrastructure.Persistence;
using Astryx.Modules.Trading.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.IntegrationTests;

/// <summary>
/// T6 paper execution (SYSTEM_DESIGN.md §15.3, PAPER-01/02, IDEM-01…05, RISK-07, TASKS.md T-084). A new user has
/// $100,000.00, so the default NVDA size is 64 shares (12% weight cap).
/// </summary>
public class PaperOrderTests(TradingApiFactory factory) : IClassFixture<TradingApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await factory.PrepareAsync();
        await factory.ScanAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record Proposed(HttpClient Client, Guid ProposalId, Guid AssessmentId);

    private async Task<Proposed> ProposeAsync(decimal last)
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        await factory.MakeQuoteLiveAsync("NVDA", last);
        var proposal = await (await client.PostAsJsonAsync("/api/v1/proposals", new { source = "signal", signalId = await factory.ActiveSignalIdAsync("NVDA") }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var assessment = proposal.GetProperty("assessment");
        assessment.GetProperty("approvable").GetBoolean().ShouldBeTrue();
        return new Proposed(client, proposal.GetProperty("id").GetGuid(), assessment.GetProperty("id").GetGuid());
    }

    private static Task<HttpResponseMessage> PlaceAsync(HttpClient client, Guid proposalId, Guid assessmentId, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/paper/orders") { Content = JsonContent.Create(new { proposalId, assessmentId }) };
        request.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(request);
    }

    private async Task<T> QueryAsync<TContext, T>(Func<TContext, Task<T>> query)
        where TContext : notnull
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<TContext>());
    }

    private async Task AssertLedgerBalancedAsync(Guid portfolioId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        (await LedgerInvariants.CheckAsync(scope.ServiceProvider.GetRequiredService<PortfolioDbContext>(), portfolioId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task PAPER_01_a_triggered_buy_stop_fills_opens_the_position_and_attaches_OCO_exits()
    {
        var p = await ProposeAsync(186.60m);

        var response = await PlaceAsync(p.Client, p.ProposalId, p.AssessmentId, $"order-{Guid.NewGuid():N}");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var order = await response.Content.ReadFromJsonAsync<JsonElement>();
        (order.GetProperty("status").GetString(), order.GetProperty("purpose").GetString(), order.GetProperty("orderType").GetString(), order.GetProperty("quantity").GetString())
            .ShouldBe(("filled", "entry", "buy_stop", "64"));
        order.GetProperty("brokerOrderId").GetString()!.ShouldMatch("^SIM-[A-Z2-7]{6}$");
        var fill = order.GetProperty("fill");
        (fill.GetProperty("price").GetString(), fill.GetProperty("referencePrice").GetString(), fill.GetProperty("slippagePerShare").GetString(), fill.GetProperty("slippage").GetString())
            .ShouldBe(("186.62", "186.60", "0.02", "1.28"));
        var attached = order.GetProperty("attached");
        (attached.GetProperty("stop").GetString(), attached.GetProperty("target").GetString()).ShouldBe(("178.20", "204.00"));
        var positionId = order.GetProperty("positionId").GetGuid();

        var position = await QueryAsync<PortfolioDbContext, Astryx.Modules.Portfolio.Domain.Position>(db => db.Positions.IgnoreQueryFilters().SingleAsync(x => x.Id == positionId));
        (position.Status, position.Quantity, position.AvgEntryPrice, position.StopPrice, position.TargetPrice).ShouldBe(("open", 64m, 186.62m, 178.20m, 204.00m));
        var portfolio = await QueryAsync<PortfolioDbContext, Astryx.Modules.Portfolio.Domain.Portfolio>(db => db.Portfolios.IgnoreQueryFilters().SingleAsync(x => x.Id == position.PortfolioId));
        portfolio.CashBalance.ShouldBe(100_000m - 11_943.68m); // 64 × 186.62
        await AssertLedgerBalancedAsync(position.PortfolioId);
        var exits = await QueryAsync<PaperDbContext, List<Astryx.Modules.Paper.Domain.Order>>(db => db.Orders.IgnoreQueryFilters().Where(o => o.PositionId == positionId && o.Purpose != "entry").ToListAsync());
        exits.Select(e => (e.Purpose, e.OrderType, e.TimeInForce, e.Status)).OrderBy(e => e.Purpose).ShouldBe([("exit_stop", "sell_stop", "gtc", "working"), ("exit_target", "limit", "gtc", "working")]);
        exits.Select(e => e.OcoGroupId).Distinct().Count().ShouldBe(1);
        (await p.Client.GetFromJsonAsync<JsonElement>($"/api/v1/proposals/{p.ProposalId}")).GetProperty("status").GetString().ShouldBe("approved");
    }

    [Fact]
    public async Task PAPER_02_an_untriggered_buy_stop_works_and_reserves_cash()
    {
        var p = await ProposeAsync(186.40m);

        var order = await (await PlaceAsync(p.Client, p.ProposalId, p.AssessmentId, $"order-{Guid.NewGuid():N}")).Content.ReadFromJsonAsync<JsonElement>();

        (order.GetProperty("status").GetString(), order.GetProperty("stopPrice").GetString()).ShouldBe(("working", "186.50"));
        order.GetProperty("fill").ValueKind.ShouldBe(JsonValueKind.Null);
        order.GetProperty("expiresAt").ValueKind.ShouldNotBe(JsonValueKind.Null); // a day order: the session close
        var userPortfolio = await QueryAsync<PaperDbContext, Guid>(db => db.Orders.IgnoreQueryFilters().Where(o => o.Id == order.GetProperty("id").GetGuid()).Select(o => o.PortfolioId).SingleAsync());
        var portfolio = await QueryAsync<PortfolioDbContext, Astryx.Modules.Portfolio.Domain.Portfolio>(db => db.Portfolios.IgnoreQueryFilters().SingleAsync(x => x.Id == userPortfolio));
        (portfolio.CashBalance, portfolio.ReservedCash).ShouldBe((100_000m, 64 * 186.52m));
        await AssertLedgerBalancedAsync(userPortfolio);

        // The working entry now counts: a fresh NVDA proposal would be a duplicate.
        await factory.MakeQuoteLiveAsync("NVDA", 186.40m);
        var again = await (await p.Client.PostAsJsonAsync("/api/v1/proposals", new { source = "signal", signalId = await factory.ActiveSignalIdAsync("NVDA") })).Content.ReadFromJsonAsync<JsonElement>();
        again.GetProperty("assessment").GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("code").GetString() == "no_duplicate").GetProperty("status").GetString().ShouldBe("fail");
    }

    [Fact]
    public async Task IDEM_01_the_same_key_twice_replays_the_original_response()
    {
        var p = await ProposeAsync(186.60m);
        var key = $"order-{Guid.NewGuid():N}";

        var first = await PlaceAsync(p.Client, p.ProposalId, p.AssessmentId, key);
        var second = await PlaceAsync(p.Client, p.ProposalId, p.AssessmentId, key);

        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.Headers.GetValues("Idempotent-Replayed").Single().ShouldBe("true");
        (await second.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
        (await QueryAsync<PaperDbContext, int>(db => db.Orders.IgnoreQueryFilters().CountAsync(o => o.ProposalId == p.ProposalId))).ShouldBe(1);
    }

    [Fact]
    public async Task IDEM_02_ten_concurrent_requests_with_one_key_place_one_order()
    {
        var p = await ProposeAsync(186.60m);
        var key = $"order-{Guid.NewGuid():N}";

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => PlaceAsync(p.Client, p.ProposalId, p.AssessmentId, key)));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created || r.StatusCode == HttpStatusCode.Conflict);
        responses.Count(r => r.StatusCode == HttpStatusCode.Created && !r.Headers.Contains("Idempotent-Replayed")).ShouldBe(1);
        var orderId = await QueryAsync<PaperDbContext, Guid>(db => db.Orders.IgnoreQueryFilters().Where(o => o.ProposalId == p.ProposalId).Select(o => o.Id).SingleAsync());
        var portfolioId = await QueryAsync<PaperDbContext, Guid>(db => db.Orders.IgnoreQueryFilters().Where(o => o.Id == orderId).Select(o => o.PortfolioId).SingleAsync());
        (await QueryAsync<PortfolioDbContext, int>(db => db.Transactions.IgnoreQueryFilters().CountAsync(t => t.PortfolioId == portfolioId && t.Type == "buy"))).ShouldBe(1);
        await AssertLedgerBalancedAsync(portfolioId);
    }

    [Fact]
    public async Task IDEM_03_and_IDEM_05_a_reused_key_or_a_second_key_are_refused()
    {
        var p = await ProposeAsync(186.60m);
        var key = $"order-{Guid.NewGuid():N}";
        (await PlaceAsync(p.Client, p.ProposalId, p.AssessmentId, key)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var reused = await PlaceAsync(p.Client, p.ProposalId, Guid.NewGuid(), key);
        reused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await reused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("idempotency_key_reused");

        var secondKey = await PlaceAsync(p.Client, p.ProposalId, p.AssessmentId, $"order-{Guid.NewGuid():N}");
        secondKey.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await secondKey.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("proposal_not_pending");
    }

    [Fact]
    public async Task RISK_07_changed_risk_settings_make_the_assessment_stale_and_nothing_is_written()
    {
        var p = await ProposeAsync(186.60m);
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/me/settings/risk")
        {
            Content = JsonContent.Create(new { maxRiskPerTradePct = "0.0075", maxPositionWeightPct = "0.10", maxSectorExposurePct = "0.35", sectorWarningPct = "0.30", dailyLossHaltPct = "0.02", maxOpenPositions = 12, minRewardRisk = "1.5" }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"v1\"");
        (await p.Client.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await PlaceAsync(p.Client, p.ProposalId, p.AssessmentId, $"order-{Guid.NewGuid():N}");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("assessment_stale");
        (await QueryAsync<PaperDbContext, int>(db => db.Orders.IgnoreQueryFilters().CountAsync(o => o.ProposalId == p.ProposalId))).ShouldBe(0);
        (await QueryAsync<TradingDbContext, string>(db => db.Proposals.IgnoreQueryFilters().Where(x => x.Id == p.ProposalId).Select(x => x.Status).SingleAsync())).ShouldBe("pending");
    }

    [Fact]
    public async Task Another_users_order_is_not_found()
    {
        var p = await ProposeAsync(186.60m);
        var orderId = (await (await PlaceAsync(p.Client, p.ProposalId, p.AssessmentId, $"order-{Guid.NewGuid():N}")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var bob = await TestUsers.SignedInClientAsync(factory);

        (await bob.GetAsync($"/api/v1/paper/orders/{orderId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await bob.GetFromJsonAsync<JsonElement>("/api/v1/paper/orders")).GetProperty("items").GetArrayLength().ShouldBe(0);
        (await p.Client.GetFromJsonAsync<JsonElement>("/api/v1/paper/orders")).GetProperty("items").EnumerateArray().Select(o => o.GetProperty("purpose").GetString()).Order()
            .ShouldBe(["entry", "exit_stop", "exit_target"]);
    }
}
