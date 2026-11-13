using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astryx.Modules.MarketData.Infrastructure.Persistence;
using Astryx.Modules.Paper.Infrastructure.Persistence;
using Astryx.Modules.Portfolio.Infrastructure.Persistence;
using Astryx.Modules.Trading.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.IntegrationTests;

/// <summary>
/// The order-path safety proof (SYSTEM_DESIGN.md §21.10 items 4–5, A5, TASKS.md T-112): <c>POST /paper/orders</c> with
/// every kind of bad assessment is refused, and nothing reaches the order book or the ledger. Each case gets a fresh
/// user, so "nothing was created" is exact: no order rows and only the initial deposit in the ledger.
/// </summary>
public class OrderPathSafetyTests(TradingApiFactory factory) : IClassFixture<TradingApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await factory.PrepareAsync();
        await factory.ScanAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record Setup(HttpClient Client, Guid PortfolioId, Guid ProposalId, Guid AssessmentId);

    /// <summary>A fresh user with a pending NVDA proposal and its approvable assessment.</summary>
    private async Task<Setup> ProposalAsync()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        await factory.MakeQuoteLiveAsync("NVDA", 186.60m);
        var proposal = await (await client.PostAsJsonAsync("/api/v1/proposals", new { source = "signal", signalId = await factory.ActiveSignalIdAsync("NVDA") })).Content.ReadFromJsonAsync<JsonElement>();
        proposal.GetProperty("assessment").GetProperty("approvable").GetBoolean().ShouldBeTrue();
        var portfolioId = (await client.GetFromJsonAsync<JsonElement>("/api/v1/portfolio")).GetProperty("id").GetGuid();
        return new Setup(client, portfolioId, proposal.GetProperty("id").GetGuid(), proposal.GetProperty("assessment").GetProperty("id").GetGuid());
    }

    private static Task<HttpResponseMessage> OrderAsync(HttpClient client, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/paper/orders") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", $"safety-{Guid.NewGuid():N}");
        return client.SendAsync(request);
    }

    private async Task SqlAsync<TContext>(FormattableString sql)
        where TContext : DbContext
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TContext>().Database.ExecuteSqlAsync(sql);
    }

    /// <summary>The refusal, then the proof: no order for the portfolio and the ledger is still just the deposit.</summary>
    private async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code, params Guid[] portfolioIds)
    {
        response.StatusCode.ShouldBe(status);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe(code);
        await using var scope = factory.Services.CreateAsyncScope();
        var paper = scope.ServiceProvider.GetRequiredService<PaperDbContext>();
        var portfolio = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        foreach (var id in portfolioIds)
        {
            (await paper.Orders.IgnoreQueryFilters().CountAsync(o => o.PortfolioId == id)).ShouldBe(0);
            (await paper.Fills.IgnoreQueryFilters().CountAsync(f => f.PortfolioId == id)).ShouldBe(0);
            (await portfolio.Transactions.IgnoreQueryFilters().Where(t => t.PortfolioId == id).Select(t => t.Type).ToListAsync()).ShouldBe(["initial_deposit"]);
            (await portfolio.Positions.IgnoreQueryFilters().CountAsync(p => p.PortfolioId == id)).ShouldBe(0);
            var state = await portfolio.Portfolios.IgnoreQueryFilters().SingleAsync(p => p.Id == id);
            (state.CashBalance, state.ReservedCash).ShouldBe((100_000m, 0m));
        }
    }

    [Fact]
    public async Task Missing_assessment_id_is_refused()
    {
        var s = await ProposalAsync();
        // An absent id binds as the empty GUID, which names no assessment.
        await AssertRefusedAsync(await OrderAsync(s.Client, new { proposalId = s.ProposalId }), HttpStatusCode.NotFound, "not_found", s.PortfolioId);
    }

    [Fact]
    public async Task Unknown_assessment_is_refused()
    {
        var s = await ProposalAsync();
        await AssertRefusedAsync(await OrderAsync(s.Client, new { proposalId = s.ProposalId, assessmentId = Guid.NewGuid() }), HttpStatusCode.NotFound, "not_found", s.PortfolioId);
    }

    [Fact]
    public async Task Another_users_proposal_and_assessment_are_refused()
    {
        var alice = await ProposalAsync();
        var bob = await ProposalAsync();

        await AssertRefusedAsync(await OrderAsync(bob.Client, new { proposalId = alice.ProposalId, assessmentId = alice.AssessmentId }), HttpStatusCode.NotFound, "not_found", alice.PortfolioId, bob.PortfolioId);
        // Bob's own proposal with Alice's assessment: it belongs to another proposal.
        await AssertRefusedAsync(await OrderAsync(bob.Client, new { proposalId = bob.ProposalId, assessmentId = alice.AssessmentId }), HttpStatusCode.NotFound, "not_found", alice.PortfolioId, bob.PortfolioId);
    }

    [Fact]
    public async Task A_failed_assessment_is_refused()
    {
        var s = await ProposalAsync();
        // 2 000 shares of NVDA is far beyond the weight cap and the cash: every money check fails.
        var failed = await (await s.Client.PostAsJsonAsync($"/api/v1/proposals/{s.ProposalId}/assessments", new { quantity = "2000", riskPerTradePct = "0.0075" })).Content.ReadFromJsonAsync<JsonElement>();
        failed.GetProperty("approvable").GetBoolean().ShouldBeFalse();

        await AssertRefusedAsync(await OrderAsync(s.Client, new { proposalId = s.ProposalId, assessmentId = failed.GetProperty("id").GetGuid() }), HttpStatusCode.Conflict, "assessment_stale", s.PortfolioId);
    }

    [Fact]
    public async Task An_expired_assessment_is_refused()
    {
        var s = await ProposalAsync();
        await SqlAsync<TradingDbContext>($"UPDATE trading.risk_assessments SET expires_at = now() - interval '1 second' WHERE id = {s.AssessmentId}");

        await AssertRefusedAsync(await OrderAsync(s.Client, new { proposalId = s.ProposalId, assessmentId = s.AssessmentId }), HttpStatusCode.Conflict, "assessment_stale", s.PortfolioId);
    }

    [Fact]
    public async Task An_expired_or_rejected_proposal_is_refused()
    {
        var expired = await ProposalAsync();
        await SqlAsync<TradingDbContext>($"UPDATE trading.proposals SET expires_at = now() - interval '1 second' WHERE id = {expired.ProposalId}");
        await AssertRefusedAsync(await OrderAsync(expired.Client, new { proposalId = expired.ProposalId, assessmentId = expired.AssessmentId }), HttpStatusCode.Conflict, "proposal_not_pending", expired.PortfolioId);

        var rejected = await ProposalAsync();
        (await rejected.Client.PostAsJsonAsync($"/api/v1/proposals/{rejected.ProposalId}/reject", new { })).EnsureSuccessStatusCode();
        await AssertRefusedAsync(await OrderAsync(rejected.Client, new { proposalId = rejected.ProposalId, assessmentId = rejected.AssessmentId }), HttpStatusCode.Conflict, "proposal_not_pending", rejected.PortfolioId);
    }

    [Fact]
    public async Task An_assessment_whose_inputs_changed_is_refused()
    {
        // Risk settings changed after the assessment: its hash names an old settings version.
        var limits = await ProposalAsync();
        var current = await limits.Client.GetAsync("/api/v1/me/settings/risk");
        var etag = current.Headers.ETag!.Tag;
        var put = new HttpRequestMessage(HttpMethod.Put, "/api/v1/me/settings/risk")
        {
            Content = JsonContent.Create(new { maxRiskPerTradePct = "0.0075", maxPositionWeightPct = "0.12", maxSectorExposurePct = "0.35", sectorWarningPct = "0.30", dailyLossHaltPct = "0.019", maxOpenPositions = 12, minRewardRisk = "2.0" }),
        };
        put.Headers.TryAddWithoutValidation("If-Match", etag);
        (await limits.Client.SendAsync(put)).EnsureSuccessStatusCode();
        await AssertRefusedAsync(await OrderAsync(limits.Client, new { proposalId = limits.ProposalId, assessmentId = limits.AssessmentId }), HttpStatusCode.Conflict, "assessment_stale", limits.PortfolioId);

        // The quote moved more than 0.5 % since the assessment.
        var moved = await ProposalAsync();
        await factory.MakeQuoteLiveAsync("NVDA", 188.00m);
        // The re-run on the new quote either drifts too far (409 assessment_stale) or now fails a check (422 risk_check_failed).
        var response = await OrderAsync(moved.Client, new { proposalId = moved.ProposalId, assessmentId = moved.AssessmentId });
        var code = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;
        code.ShouldBeOneOf("assessment_stale", "risk_check_failed");
        var status = code == "assessment_stale" ? HttpStatusCode.Conflict : HttpStatusCode.UnprocessableEntity;
        response.StatusCode.ShouldBe(status);
        await AssertRefusedAsync(await OrderAsync(moved.Client, new { proposalId = moved.ProposalId, assessmentId = moved.AssessmentId }), status, code, moved.PortfolioId);
    }

    [Fact]
    public async Task A_stale_quote_is_refused()
    {
        var s = await ProposalAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
            await db.Quotes.Where(q => db.Instruments.Any(i => i.Id == q.InstrumentId && i.Symbol == "NVDA"))
                .ExecuteUpdateAsync(u => u.SetProperty(q => q.AsOf, DateTimeOffset.UtcNow.AddHours(-2)));
        }

        try
        {
            await AssertRefusedAsync(await OrderAsync(s.Client, new { proposalId = s.ProposalId, assessmentId = s.AssessmentId }), HttpStatusCode.Conflict, "quote_stale", s.PortfolioId);
        }
        finally
        {
            await factory.MakeQuoteLiveAsync("NVDA", 186.60m);
        }
    }

    [Fact]
    public async Task With_the_market_closed_nothing_executes()
    {
        var s = await ProposalAsync();
        factory.Session.CloseIn = null;
        try
        {
            await AssertRefusedAsync(await OrderAsync(s.Client, new { proposalId = s.ProposalId, assessmentId = s.AssessmentId }), HttpStatusCode.Conflict, "market_closed", s.PortfolioId);
        }
        finally
        {
            factory.Session.CloseIn = TimeSpan.FromHours(3);
        }
    }

    [Fact]
    public async Task Features_LiveTrading_true_keeps_the_API_from_starting()
    {
        var live = new LiveTradingApiFactory();
        await live.InitializeAsync();
        try
        {
            var start = () => live.CreateClient();

            start.ShouldThrow<Exception>().ToString().ShouldContain("Features:LiveTrading is enabled");
        }
        finally
        {
            await live.DisposeAsync();
        }
    }

    /// <summary>The same app with the live flag on: the startup guard (§17.9) must refuse to run.</summary>
    private sealed class LiveTradingApiFactory : ApiFactory
    {
        protected override IReadOnlyDictionary<string, string> ExtraSettings => new Dictionary<string, string> { ["Features:LiveTrading"] = "true" };
    }
}
