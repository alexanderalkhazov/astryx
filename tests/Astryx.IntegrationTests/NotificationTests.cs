using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astryx.Contracts.Events;
using Astryx.Contracts.Events.Paper;
using Astryx.Infrastructure.Outbox;
using Astryx.Modules.Notifications.Application;
using Astryx.Modules.Notifications.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.IntegrationTests;

/// <summary>The Activity feed (SYSTEM_DESIGN.md §7.15, §11.10 N1–N3, IDEM-06, TASKS.md T-095).</summary>
public class NotificationTests(TradingApiFactory factory) : IClassFixture<TradingApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await factory.PrepareAsync();
        await factory.ScanAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task DispatchAsync()
    {
        var dispatcher = factory.Services.GetRequiredService<OutboxDispatcher>();
        while (await dispatcher.DispatchOnceAsync(default) > 0)
        {
        }
    }

    private async Task<(HttpClient Client, Guid UserId)> FilledAsync()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        var userId = (await client.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("id").GetGuid();
        await factory.MakeQuoteLiveAsync("NVDA", 186.60m);
        var proposal = await (await client.PostAsJsonAsync("/api/v1/proposals", new { source = "signal", signalId = await factory.ActiveSignalIdAsync("NVDA") })).Content.ReadFromJsonAsync<JsonElement>();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/paper/orders")
        {
            Content = JsonContent.Create(new { proposalId = proposal.GetProperty("id").GetGuid(), assessmentId = proposal.GetProperty("assessment").GetProperty("id").GetGuid() }),
        };
        request.Headers.Add("Idempotency-Key", $"order-{Guid.NewGuid():N}");
        (await client.SendAsync(request)).EnsureSuccessStatusCode();
        await DispatchAsync();
        return (client, userId);
    }

    [Fact]
    public async Task A_fill_lands_in_the_feed_with_a_deep_link_and_can_be_read()
    {
        var (client, _) = await FilledAsync();

        var page = await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications");

        var item = page.GetProperty("items").EnumerateArray().Single();
        (item.GetProperty("category").GetString(), item.GetProperty("type").GetString(), item.GetProperty("title").GetString(), item.GetProperty("deepLink").GetString(), item.GetProperty("read").GetBoolean())
            .ShouldBe(("orders", "order_filled", "Filled (simulated) · NVDA", "/position/NVDA", false));
        item.GetProperty("body").GetString().ShouldBe("Bought 64 NVDA at $186.62 in your paper account.");
        page.GetProperty("unreadCount").GetInt32().ShouldBe(1);
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications?category=alerts")).GetProperty("items").GetArrayLength().ShouldBe(0);

        (await client.PostAsync($"/api/v1/notifications/{item.GetProperty("id").GetGuid()}/read", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications")).GetProperty("unreadCount").GetInt32().ShouldBe(0);
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications?unreadOnly=true")).GetProperty("items").GetArrayLength().ShouldBe(0);
        (await client.GetAsync("/api/v1/notifications?category=nope")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task IDEM_06_a_redelivered_event_creates_one_notification_and_read_all_clears_them()
    {
        var (client, userId) = await FilledAsync();
        var closed = new PositionClosed(userId, Guid.NewGuid(), "NVDA", "stop", 174.98m, -744.96m, GappedThroughStop: true);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<NotificationWriter>();
            var context = new IntegrationEventContext(Guid.NewGuid(), DateTimeOffset.UtcNow, null);
            await writer.HandleAsync(closed, context, default);
            await writer.HandleAsync(closed, context, default);
        }

        var items = (await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications")).GetProperty("items").EnumerateArray().ToList();
        var stop = items.Single(i => i.GetProperty("type").GetString() == "stop_hit");
        (stop.GetProperty("title").GetString(), stop.GetProperty("body").GetString()).ShouldBe(("Stop hit · NVDA", "Sold at $174.98 · realized −$744.96. Stop filled below plan (gap)."));

        (await client.PostAsJsonAsync("/api/v1/notifications/read-all", new { })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications")).GetProperty("unreadCount").GetInt32().ShouldBe(0);
        var bob = await TestUsers.SignedInClientAsync(factory);
        (await bob.PostAsync($"/api/v1/notifications/{stop.GetProperty("id").GetGuid()}/read", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await bob.GetFromJsonAsync<JsonElement>("/api/v1/notifications")).GetProperty("items").GetArrayLength().ShouldBe(0);
    }
}
