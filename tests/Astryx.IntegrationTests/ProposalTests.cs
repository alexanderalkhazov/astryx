using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astryx.Contracts.MarketData;
using Astryx.Infrastructure.Jobs;
using Astryx.Modules.Ai.Infrastructure.Fake;
using Astryx.Modules.Discovery.Application.Scanner;
using Astryx.Modules.Discovery.Infrastructure.Persistence;
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

    protected override IReadOnlyDictionary<string, string> ExtraSettings => new Dictionary<string, string> { ["Ai:Provider"] = "Fake" };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.AddSingleton<IMarketSessionClock>(Session));
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
        p.GetProperty("assessment").ValueKind.ShouldBe(JsonValueKind.Null); // arrives with the risk engine (T-083)
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
