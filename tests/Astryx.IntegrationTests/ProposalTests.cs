using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astryx.Contracts.MarketData;
using Astryx.Infrastructure.Jobs;
using Astryx.Modules.Ai.Infrastructure.Fake;
using Astryx.Modules.Discovery.Application.Scanner;
using Astryx.Modules.Discovery.Infrastructure.Persistence;
using Astryx.Modules.MarketData.Infrastructure.Persistence;
using Astryx.Modules.Trading.Application;
using Astryx.Modules.Trading.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.IntegrationTests;

/// <summary>A regular session that tests can open and close at will, on the real clock.</summary>
public sealed class TestSessionClock : IMarketSessionClock
{
    public DateTimeOffset Now => DateTimeOffset.UtcNow;

    /// <summary>Open from an hour ago until <see cref="CloseIn"/> from now (null: no session today).</summary>
    public TimeSpan? CloseIn { get; set; } = TimeSpan.FromHours(3);

    public Task<TradingSession?> TodayAsync(CancellationToken cancellationToken) =>
        Task.FromResult(CloseIn is { } close ? new TradingSession(DateOnly.FromDateTime(Now.UtcDateTime), Now.AddHours(-1), Now + close, false) : null);
}

/// <summary>
/// The synced fixture market on the real clock (quotes fresh, NVDA's Base Breakout signal live once scanned), a
/// session that is open by default, and the scripted AI provider.
/// </summary>
public class TradingApiFactory : SyncedMarketApiFactory
{
    public TestSessionClock Session { get; } = new();

    protected override IReadOnlyDictionary<string, string> ExtraSettings => new Dictionary<string, string>
    {
        ["Ai:Provider"] = "Fake",
        ["RateLimits:OrdersPerMinute"] = "1000",
        ["RateLimits:AssessmentPerMinute"] = "1000",
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.AddSingleton<IMarketSessionClock>(Session));
    }

    /// <summary>Stamps the symbol's stored quote as just received, as during a live session (the fake feed stamps closes after hours).</summary>
    public async Task MakeQuoteLiveAsync(string symbol, decimal? last = null)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var now = DateTimeOffset.UtcNow;
        var quotes = db.Quotes.Where(q => db.Instruments.Any(i => i.Id == q.InstrumentId && i.Symbol == symbol));
        await quotes.ExecuteUpdateAsync(s => s.SetProperty(q => q.AsOf, now).SetProperty(q => q.QuoteSeq, q => q.QuoteSeq + 1));
        if (last is { } price)
        {
            await quotes.ExecuteUpdateAsync(s => s.SetProperty(q => q.Last, price));
        }
    }

    public async Task PrepareAsync()
    {
        await EnsureSyncedAsync();
        await RefreshQuotesAsync();
    }

    public FakeAiProvider Fake => Services.GetRequiredService<FakeAiProvider>();

    public async Task ScanAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ScannerRunHandler>().HandleAsync(new ScannerRunJob("manual"), new JobContext(Guid.NewGuid(), 1, 1, null), default);
    }

    public async Task<Guid> ActiveSignalIdAsync(string symbol)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DiscoveryDbContext>().Signals.Where(s => s.Symbol == symbol && s.Status == "active").Select(s => s.Id).SingleAsync();
    }
}

/// <summary>T1–T3 and T5 (SYSTEM_DESIGN.md §7.9, §11.7, TASKS.md T-081).</summary>
public class ProposalTests(TradingApiFactory factory) : IClassFixture<TradingApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await factory.PrepareAsync();
        await factory.ScanAsync();
        await AnalysisSteps.DrainAsync(factory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static Task<HttpResponseMessage> FromSignalAsync(HttpClient client, Guid signalId) =>
        client.PostAsJsonAsync("/api/v1/proposals", new { source = "signal", signalId });

    [Fact]
    public async Task The_NVDA_signal_becomes_the_design_proposal()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        var signalId = await factory.ActiveSignalIdAsync("NVDA");

        var response = await FromSignalAsync(client, signalId);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var p = await JsonAsync(response);
        (p.GetProperty("status").GetString(), p.GetProperty("label").GetString(), p.GetProperty("symbol").GetString(), p.GetProperty("name").GetString())
            .ShouldBe(("pending", "RESEARCH PROPOSAL · NOT EXECUTED", "NVDA", "NVIDIA Corp."));
        (p.GetProperty("side").GetString(), p.GetProperty("direction").GetString(), p.GetProperty("orderType").GetString(), p.GetProperty("timeInForce").GetString())
            .ShouldBe(("buy", "long", "buy_stop", "day"));
        (p.GetProperty("entry").GetString(), p.GetProperty("stop").GetString(), p.GetProperty("target").GetString()).ShouldBe(("186.50", "178.20", "204.00"));
        (p.GetProperty("stopPct").GetString(), p.GetProperty("targetPct").GetString()).ShouldBe(("-0.0445", "0.0938"));
        var source = p.GetProperty("source");
        (source.GetProperty("type").GetString(), source.GetProperty("signalId").GetGuid(), source.GetProperty("strategyName").GetString()).ShouldBe(("signal", signalId, "Base Breakout v3"));
        var analysis = p.GetProperty("analysis");
        (analysis.GetProperty("headline").GetString(), analysis.GetProperty("score").GetString(), analysis.GetProperty("copySource").GetString()).ShouldBe(("Breaking out of its base", "87", "template"));
        analysis.GetProperty("summary").GetString().ShouldBe("Cleared a 6-week base at $185.90 on 2.4× average volume.");
        analysis.GetProperty("evidence")[0].GetString().ShouldBe("Volume 2.4× its 20-day average");
        analysis.GetProperty("invalidation")[0].GetString().ShouldBe("A close below the stop at 178.20.");
        var expected = DateTimeOffset.UtcNow + factory.Session.CloseIn!.Value - TimeSpan.FromMinutes(30); // the close − 30 min
        p.GetProperty("expiresAt").GetDateTimeOffset().ShouldBe(expected, TimeSpan.FromSeconds(30));
        p.GetProperty("assessment").GetProperty("sizedBy").GetString().ShouldBe("weight_cap"); // the initial default-sized assessment (T-083)
    }

    [Fact]
    public async Task One_pending_proposal_per_instrument_and_rejecting_frees_it()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        var signalId = await factory.ActiveSignalIdAsync("NVDA");
        var first = await JsonAsync(await FromSignalAsync(client, signalId));
        var id = first.GetProperty("id").GetGuid();

        var again = await FromSignalAsync(client, signalId);
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonAsync(again)).GetProperty("id").GetGuid().ShouldBe(id);
        (await client.GetFromJsonAsync<JsonElement>($"/api/v1/proposals/{id}")).GetProperty("status").GetString().ShouldBe("pending");
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/proposals?status=pending")).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ShouldBe([id]);

        var rejected = await client.PostAsJsonAsync($"/api/v1/proposals/{id}/reject", new { reason = "Earnings too close" });

        rejected.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await JsonAsync(rejected);
        (body.GetProperty("status").GetString(), body.GetProperty("rejectReason").GetString()).ShouldBe(("rejected", "user_rejected"));
        var twice = await client.PostAsJsonAsync($"/api/v1/proposals/{id}/reject", new { });
        twice.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await JsonAsync(twice)).GetProperty("code").GetString().ShouldBe("proposal_not_pending");
        (await FromSignalAsync(client, signalId)).StatusCode.ShouldBe(HttpStatusCode.Created);
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<DiscoveryDbContext>().Signals.SingleAsync(s => s.Id == signalId)).Status.ShouldBe("active"); // kept for hit-rate audits
    }

    [Fact]
    public async Task Unknown_and_inactive_signals_are_refused()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        (await FromSignalAsync(client, Guid.NewGuid())).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var old = await factory.ActiveSignalIdAsync("NVDA");
        await factory.ScanAsync(); // supersedes it

        var response = await FromSignalAsync(client, old);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await JsonAsync(response)).GetProperty("code").GetString().ShouldBe("signal_not_active");
        (await client.PostAsJsonAsync("/api/v1/proposals", new { source = "signal" })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    private async Task<Guid> AnalysisResultAsync(HttpClient client, FakeAiScenario scenario)
    {
        factory.Fake.Enqueue(scenario);
        var jobId = (await JsonAsync(await AnalysisSteps.RequestAsync(client))).GetProperty("id").GetGuid();
        await AnalysisSteps.DrainAsync(factory);
        return (await client.GetFromJsonAsync<JsonElement>($"/api/v1/analyses/jobs/{jobId}")).GetProperty("resultId").GetGuid();
    }

    [Fact]
    public async Task A_validated_analysis_seeds_a_proposal_at_the_top_of_its_entry_zone()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        var analysisId = await AnalysisResultAsync(client, FakeAiScenario.Valid);

        var response = await client.PostAsJsonAsync("/api/v1/proposals", new { source = "analysis", analysisId });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var p = await JsonAsync(response);
        (p.GetProperty("entry").GetString(), p.GetProperty("stop").GetString(), p.GetProperty("target").GetString(), p.GetProperty("orderType").GetString())
            .ShouldBe(("187.50", "178.20", "204.00", "buy_stop")); // above the 186.40 last
        (p.GetProperty("source").GetProperty("analysisId").GetGuid(), p.GetProperty("analysis").GetProperty("copySource").GetString()).ShouldBe((analysisId, "ai"));
        p.GetProperty("analysis").GetProperty("headline").GetString().ShouldBe("Bullish case from the AI analysis");
    }

    [Fact]
    public async Task Rejected_ai_levels_and_foreign_analyses_cannot_seed_a_proposal()
    {
        var alice = await TestUsers.SignedInClientAsync(factory);
        var bob = await TestUsers.SignedInClientAsync(factory);
        var hallucinated = await AnalysisResultAsync(alice, FakeAiScenario.HallucinatedNumbers);

        var response = await alice.PostAsJsonAsync("/api/v1/proposals", new { source = "analysis", analysisId = hallucinated });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(response)).GetProperty("code").GetString().ShouldBe("ai_levels_rejected");
        (await bob.PostAsJsonAsync("/api/v1/proposals", new { source = "analysis", analysisId = hallucinated })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Another_users_proposal_is_not_found()
    {
        var alice = await TestUsers.SignedInClientAsync(factory);
        var bob = await TestUsers.SignedInClientAsync(factory);
        var id = (await JsonAsync(await FromSignalAsync(alice, await factory.ActiveSignalIdAsync("NVDA")))).GetProperty("id").GetGuid();

        (await bob.GetAsync($"/api/v1/proposals/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await bob.PostAsJsonAsync($"/api/v1/proposals/{id}/reject", new { })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await bob.GetFromJsonAsync<JsonElement>("/api/v1/proposals")).GetProperty("items").GetArrayLength().ShouldBe(0);
    }
}

/// <summary>Expiry and the closing buffer, on their own host (they move the session).</summary>
public class ProposalExpiryTests(TradingApiFactory factory) : IClassFixture<TradingApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await factory.PrepareAsync();
        await factory.ScanAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Pending_proposals_expire_and_none_can_be_created_in_the_last_30_minutes()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        var signalId = await factory.ActiveSignalIdAsync("NVDA");
        var id = (await (await client.PostAsJsonAsync("/api/v1/proposals", new { source = "signal", signalId })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TradingDbContext>();
            await db.Proposals.IgnoreQueryFilters().Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
            await scope.ServiceProvider.GetRequiredService<ProposalExpiryHandler>().HandleAsync(new ProposalExpiryJob(), new JobContext(Guid.NewGuid(), 1, 1, null), default);
        }

        var expired = await client.GetFromJsonAsync<JsonElement>($"/api/v1/proposals/{id}");
        (expired.GetProperty("status").GetString(), expired.GetProperty("rejectReason").GetString()).ShouldBe(("expired", "expired"));
        (await client.PostAsJsonAsync($"/api/v1/proposals/{id}/reject", new { })).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        factory.Session.CloseIn = TimeSpan.FromMinutes(29);
        var late = await client.PostAsJsonAsync("/api/v1/proposals", new { source = "signal", signalId });
        late.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await late.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("market_closed");
        factory.Session.CloseIn = null; // weekend or holiday
        (await client.PostAsJsonAsync("/api/v1/proposals", new { source = "signal", signalId })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        factory.Session.CloseIn = TimeSpan.FromHours(3);
    }
}

/// <summary>T4 risk assessments (SYSTEM_DESIGN.md §11.7, §14.5, TASKS.md T-083). A new user has $100,000.00 cash and no positions.</summary>
public class AssessmentTests(TradingApiFactory factory) : IClassFixture<TradingApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await factory.PrepareAsync();
        await factory.ScanAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(HttpClient Client, JsonElement Proposal)> ProposeAsync()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        await factory.MakeQuoteLiveAsync("NVDA"); // data_fresh needs a quote under 90 s old
        var response = await client.PostAsJsonAsync("/api/v1/proposals", new { source = "signal", signalId = await factory.ActiveSignalIdAsync("NVDA") });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (client, await response.Content.ReadFromJsonAsync<JsonElement>());
    }

    private static Task<HttpResponseMessage> AssessAsync(HttpClient client, Guid proposalId, object body) =>
        client.PostAsJsonAsync($"/api/v1/proposals/{proposalId}/assessments", body);

    private static JsonElement Check(JsonElement assessment, string code) => assessment.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("code").GetString() == code);

    [Fact]
    public async Task A_new_proposal_carries_a_default_sized_assessment()
    {
        var (_, proposal) = await ProposeAsync();

        var a = proposal.GetProperty("assessment");
        (a.GetProperty("quantity").GetString(), a.GetProperty("sizedBy").GetString(), a.GetProperty("riskPerTradePct").GetString()).ShouldBe(("64", "weight_cap", "0.0075"));
        var limits = a.GetProperty("limits");
        (limits.GetProperty("byRisk").GetString(), limits.GetProperty("byWeightCap").GetString(), limits.GetProperty("byCash").GetString()).ShouldBe(("90", "64", "536"));
        (a.GetProperty("equity").GetString(), a.GetProperty("cashAvailable").GetString()).ShouldBe(("100000.00", "100000.00"));
        (a.GetProperty("capitalRequired").GetString(), a.GetProperty("maxLoss").GetString(), a.GetProperty("potentialProfit").GetString(), a.GetProperty("rewardRisk").GetString())
            .ShouldBe(("11936.00", "531.20", "1120.00", "2.11"));
        a.GetProperty("checks").EnumerateArray().Take(6).Select(c => c.GetProperty("code").GetString()).ShouldBe(["risk_per_trade", "position_weight", "sector_exposure", "cash_available", "daily_loss_halt", "session_open"]);
        a.GetProperty("checks").GetArrayLength().ShouldBe(12);
        Check(a, "position_weight").GetProperty("status").GetString().ShouldBe("warn"); // 11.94%, inside 10.8–12%
        a.GetProperty("checks").EnumerateArray().Where(c => c.GetProperty("status").GetString() == "fail").Select(c => c.GetProperty("message").GetString()).ShouldBeEmpty();
        (a.GetProperty("approvable").GetBoolean(), a.GetProperty("stale").GetBoolean()).ShouldBe((true, false));
        (a.GetProperty("expiresAt").GetDateTimeOffset() - a.GetProperty("createdAt").GetDateTimeOffset()).ShouldBe(TimeSpan.FromSeconds(60));
        a.GetProperty("inputsHash").GetString()!.Length.ShouldBe(64);
    }

    [Fact]
    public async Task Overriding_the_quantity_is_assessed_and_stored_even_when_it_fails()
    {
        var (client, proposal) = await ProposeAsync();
        var id = proposal.GetProperty("id").GetGuid();

        var response = await AssessAsync(client, id, new { quantity = "70", riskPerTradePct = "0.0075" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var a = await response.Content.ReadFromJsonAsync<JsonElement>();
        (a.GetProperty("quantity").GetString(), a.GetProperty("sizedBy").GetString(), a.GetProperty("approvable").GetBoolean()).ShouldBe(("70", "user", false));
        Check(a, "position_weight").GetProperty("message").GetString().ShouldBe("Position weight 13.06% exceeds 12% cap. Max 64 shares.");
        (await client.GetFromJsonAsync<JsonElement>($"/api/v1/proposals/{id}")).GetProperty("assessment").GetProperty("id").GetGuid().ShouldBe(a.GetProperty("id").GetGuid());
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<TradingDbContext>().RiskAssessments.IgnoreQueryFilters().CountAsync(r => r.ProposalId == id)).ShouldBe(2);
    }

    [Theory]
    [InlineData("76", "0.0100")] // RISK-06: above the user's 0.75% max
    [InlineData("76", "0.0060")] // not one of the choices
    [InlineData("1.5", "0.0075")]
    [InlineData("0", "0.0075")]
    public async Task Choices_outside_the_allowed_set_or_the_users_max_are_422(string quantity, string risk)
    {
        var (client, proposal) = await ProposeAsync();

        var response = await AssessAsync(client, proposal.GetProperty("id").GetGuid(), new { quantity, riskPerTradePct = risk });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("validation_failed");
    }

    [Fact]
    public async Task Changing_risk_settings_makes_the_existing_assessment_stale()
    {
        var (client, proposal) = await ProposeAsync();
        var id = proposal.GetProperty("id").GetGuid();
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/me/settings/risk")
        {
            Content = JsonContent.Create(new { maxRiskPerTradePct = "0.005", maxPositionWeightPct = "0.12", maxSectorExposurePct = "0.35", sectorWarningPct = "0.30", dailyLossHaltPct = "0.02", maxOpenPositions = 12, minRewardRisk = "1.5" }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"v1\"");
        (await client.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var after = await client.GetFromJsonAsync<JsonElement>($"/api/v1/proposals/{id}");

        after.GetProperty("assessment").GetProperty("stale").GetBoolean().ShouldBeTrue();
        (await AssessAsync(client, id, new { riskPerTradePct = "0.0075" })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity); // now above the new max
        var fresh = await (await AssessAsync(client, id, new { riskPerTradePct = "0.005" })).Content.ReadFromJsonAsync<JsonElement>();
        (fresh.GetProperty("stale").GetBoolean(), fresh.GetProperty("limits").GetProperty("byRisk").GetString()).ShouldBe((false, "60"));
    }

    [Fact]
    public async Task Decided_proposals_cannot_be_assessed()
    {
        var (client, proposal) = await ProposeAsync();
        var id = proposal.GetProperty("id").GetGuid();
        await client.PostAsJsonAsync($"/api/v1/proposals/{id}/reject", new { });

        var response = await AssessAsync(client, id, new { riskPerTradePct = "0.0075" });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("proposal_not_pending");
    }
}

/// <summary>Its own database: these tests supersede the NVDA signal, as a later scanner run would.</summary>
public class ProposalSignalTurnoverTests(TradingApiFactory factory) : IClassFixture<TradingApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await factory.PrepareAsync();
        await factory.ScanAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>The scanner's next run on moved quotes: the active signal is re-issued with a new id.</summary>
    private async Task<Guid> ReissueAsync(Guid signalId)
    {
        await factory.MakeQuoteLiveAsync("NVDA", 186.61m);
        await factory.ScanAsync();
        var current = await factory.ActiveSignalIdAsync("NVDA");
        current.ShouldNotBe(signalId);
        return current;
    }

    [Fact]
    public async Task A_pending_proposal_on_a_superseded_signal_gives_way_to_the_current_signal()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        await factory.MakeQuoteLiveAsync("NVDA", 186.60m);
        var original = await factory.ActiveSignalIdAsync("NVDA");
        var first = await (await client.PostAsJsonAsync("/api/v1/proposals", new { source = "signal", signalId = original })).Content.ReadFromJsonAsync<JsonElement>();
        var current = await ReissueAsync(original);

        var response = await client.PostAsJsonAsync("/api/v1/proposals", new { source = "signal", signalId = current });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var second = await response.Content.ReadFromJsonAsync<JsonElement>();
        second.GetProperty("id").GetGuid().ShouldNotBe(first.GetProperty("id").GetGuid());
        second.GetProperty("assessment").GetProperty("approvable").GetBoolean().ShouldBeTrue();
        (await client.GetFromJsonAsync<JsonElement>($"/api/v1/proposals/{first.GetProperty("id").GetGuid()}")).GetProperty("status").GetString().ShouldBe("expired");
        // The same current signal again: the new pending proposal answers.
        (await client.PostAsJsonAsync("/api/v1/proposals", new { source = "signal", signalId = current })).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
