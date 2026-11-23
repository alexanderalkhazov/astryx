using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astryx.Modules.Invest.Application;
using Astryx.Modules.Invest.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.IntegrationTests;

/// <summary>The Invest track end to end on the synced fake market (TASKS.md T-164).</summary>
public class InvestTests(TradingApiFactory factory) : IClassFixture<TradingApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.PrepareAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", $"key-{Guid.NewGuid():N}");
        return await client.SendAsync(request);
    }

    private static decimal Dec(JsonElement e, string name) => decimal.Parse(e.GetProperty(name).GetString()!, System.Globalization.CultureInfo.InvariantCulture);

    private static decimal Weight(JsonElement plan, string symbol) => Dec(plan.GetProperty("funds").EnumerateArray().Single(f => f.GetProperty("symbol").GetString() == symbol), "weight");

    [Fact]
    public async Task A_plan_is_created_invested_in_its_mix_rebalanced_after_a_change_and_topped_up_on_schedule()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        (await client.GetAsync("/api/v1/invest")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var created = await SendAsync(client, HttpMethod.Put, "/api/v1/invest", new { profile = "balanced", initialDeposit = "10000", contributionAmount = "100", contributionFrequency = "weekly" });
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var plan = await created.Content.ReadFromJsonAsync<JsonElement>();
        Dec(plan, "contributed").ShouldBe(10_000m);
        Dec(plan, "cash").ShouldBeLessThan(1m); // invested at once, fractional shares
        Weight(plan, "SPY").ShouldBe(0.50m, 0.01m);
        Weight(plan, "BND").ShouldBe(0.30m, 0.01m);
        plan.GetProperty("rebalanceSuggested").GetBoolean().ShouldBeFalse();

        var cautious = await (await SendAsync(client, HttpMethod.Put, "/api/v1/invest", new { profile = "cautious", contributionAmount = "100", contributionFrequency = "weekly" })).Content.ReadFromJsonAsync<JsonElement>();
        cautious.GetProperty("rebalanceSuggested").GetBoolean().ShouldBeTrue(); // 30 % bonds held, 60 % wanted
        Dec(cautious, "contributed").ShouldBe(10_000m); // changing the mix deposits nothing

        var rebalanced = await (await SendAsync(client, HttpMethod.Post, "/api/v1/invest/rebalance")).Content.ReadFromJsonAsync<JsonElement>();
        Weight(rebalanced, "BND").ShouldBe(0.60m, 0.01m);
        rebalanced.GetProperty("rebalanceSuggested").GetBoolean().ShouldBeFalse();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InvestDbContext>();
            await db.Plans.IgnoreQueryFilters().ExecuteUpdateAsync(s => s.SetProperty(p => p.NextContributionOn, new DateOnly(2020, 1, 6))); // long overdue
            (await scope.ServiceProvider.GetRequiredService<InvestService>().ContributeDueAsync(default)).ShouldBeGreaterThanOrEqualTo(1);
        }

        var topped = await client.GetFromJsonAsync<JsonElement>("/api/v1/invest");
        Dec(topped, "contributed").ShouldBe(10_100m); // one contribution, not a backlog of years
        DateOnly.Parse(topped.GetProperty("contribution").GetProperty("nextOn").GetString()!, System.Globalization.CultureInfo.InvariantCulture)
            .ShouldBeGreaterThan(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)));
        topped.GetProperty("activity").EnumerateArray().First().GetProperty("kind").GetString().ShouldBeOneOf("buy", "deposit");

        var other = await TestUsers.SignedInClientAsync(factory);
        (await other.GetAsync("/api/v1/invest")).StatusCode.ShouldBe(HttpStatusCode.NotFound); // plans are per user
    }

    [Fact]
    public async Task Invalid_plans_are_refused()
    {
        var client = await TestUsers.SignedInClientAsync(factory);

        (await SendAsync(client, HttpMethod.Put, "/api/v1/invest", new { profile = "yolo", contributionAmount = "0", contributionFrequency = "none" })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await SendAsync(client, HttpMethod.Put, "/api/v1/invest", new { profile = "growth", contributionAmount = "5", contributionFrequency = "weekly" })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }
}
