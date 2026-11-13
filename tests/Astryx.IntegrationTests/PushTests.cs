using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astryx.Infrastructure.Jobs;
using Astryx.Infrastructure.Outbox;
using Astryx.Modules.Notifications.Application;
using Astryx.Modules.Notifications.Infrastructure.Persistence;
using Astryx.Modules.Notifications.Infrastructure.Push;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.IntegrationTests;

/// <summary>The trading fixture with push on and the recording gateway.</summary>
public sealed class PushApiFactory : TradingApiFactory
{
    protected override IReadOnlyDictionary<string, string> ExtraSettings =>
        new Dictionary<string, string>(base.ExtraSettings) { ["Features:Push"] = "true", ["Push:Provider"] = "fake" };

    public FakePushGateway Gateway => Services.GetRequiredService<FakePushGateway>();
}

/// <summary>N4/N5 device registration and the PushSender (SYSTEM_DESIGN.md §7.15, §11.10, TASKS.md T-101).</summary>
public class PushTests(PushApiFactory factory) : IClassFixture<PushApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await factory.PrepareAsync();
        await factory.ScanAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Token() => $"ExponentPushToken[{Guid.NewGuid():N}]";

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string deviceId, string token, string platform = "ios") =>
        client.PostAsJsonAsync("/api/v1/devices", new { deviceId, pushToken = token, platform });

    private static async Task EnablePushAsync(HttpClient client, bool fills = true) =>
        (await client.PutAsJsonAsync("/api/v1/me/settings/notifications", new { fills, signals = true, stopProximity = true, strategyGuard = true, system = true, dailySummary = false, pushEnabled = true }))
            .EnsureSuccessStatusCode();

    /// <summary>Fills 64 NVDA for a new user: one "Filled (simulated)" Activity item. Returns that item's id.</summary>
    private async Task<(HttpClient Client, Guid NotificationId)> FilledAsync(Func<HttpClient, Task> before)
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        await before(client);
        await factory.MakeQuoteLiveAsync("NVDA", 186.60m);
        var proposal = await (await client.PostAsJsonAsync("/api/v1/proposals", new { source = "signal", signalId = await factory.ActiveSignalIdAsync("NVDA") })).Content.ReadFromJsonAsync<JsonElement>();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/paper/orders")
        {
            Content = JsonContent.Create(new { proposalId = proposal.GetProperty("id").GetGuid(), assessmentId = proposal.GetProperty("assessment").GetProperty("id").GetGuid() }),
        };
        request.Headers.Add("Idempotency-Key", $"key-{Guid.NewGuid():N}");
        (await client.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var dispatcher = factory.Services.GetRequiredService<OutboxDispatcher>();
        while (await dispatcher.DispatchOnceAsync(default) > 0)
        {
        }

        var item = (await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications?category=orders")).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("type").GetString() == "order_filled");
        return (client, item.GetProperty("id").GetGuid());
    }

    private async Task PushAsync(Guid notificationId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PushSender>().HandleAsync(new PushNotificationJob(notificationId), new JobContext(Guid.NewGuid(), 1, 3, null), default);
    }

    private async Task<T> Notify<T>(Func<NotificationsDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<NotificationsDbContext>());
    }

    [Fact]
    public async Task A_fill_is_pushed_once_to_each_live_device_with_its_deep_link()
    {
        var (phone, tablet) = (Token(), Token());
        var (_, id) = await FilledAsync(async c =>
        {
            await EnablePushAsync(c);
            (await RegisterAsync(c, "device-phone-01", phone)).StatusCode.ShouldBe(HttpStatusCode.Created);
            (await RegisterAsync(c, "device-tablet-1", tablet, "android")).StatusCode.ShouldBe(HttpStatusCode.Created);
        });

        await PushAsync(id);
        await PushAsync(id); // a retry (or the worker) never sends twice

        var sent = factory.Gateway.Sent.Where(m => m.To == phone || m.To == tablet).ToList();
        sent.Count.ShouldBe(2);
        sent.ShouldAllBe(m => m.Title == "Filled (simulated) · NVDA" && m.Data["deepLink"] == "/position/NVDA" && m.Data["notificationId"] == id.ToString());
        (await Notify(db => db.Notifications.IgnoreQueryFilters().Where(n => n.Id == id).Select(n => n.PushedAt).SingleAsync())).ShouldNotBeNull();
        var queued = await Notify(db => db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM infra.job_queue WHERE type = 'notifications.push.v1' AND dedupe_key = {"push:" + id}").SingleAsync());
        queued.ShouldBe(1); // the writer enqueued the job
    }

    [Fact]
    public async Task Preferences_decide_master_switch_and_category()
    {
        var token = Token();
        var (client, id) = await FilledAsync(async c =>
        {
            await EnablePushAsync(c, fills: false);
            await RegisterAsync(c, "device-prefs-01", token);
        });

        await PushAsync(id);
        factory.Gateway.Sent.ShouldNotContain(m => m.To == token);
        (await Notify(db => db.Notifications.IgnoreQueryFilters().Where(n => n.Id == id).Select(n => n.PushedAt).SingleAsync())).ShouldBeNull();

        (await client.PutAsJsonAsync("/api/v1/me/settings/notifications", new { fills = true, signals = true, stopProximity = true, strategyGuard = true, system = true, dailySummary = false, pushEnabled = false }))
            .EnsureSuccessStatusCode();
        await PushAsync(id);
        factory.Gateway.Sent.ShouldNotContain(m => m.To == token);
    }

    [Fact]
    public async Task DeviceNotRegistered_revokes_the_token_and_N5_stops_pushes()
    {
        var (dead, alive) = (Token(), Token());
        factory.Gateway.Unregistered[dead] = true;
        var (client, id) = await FilledAsync(async c =>
        {
            await EnablePushAsync(c);
            await RegisterAsync(c, "device-dead-001", dead);
            await RegisterAsync(c, "device-alive-01", alive);
        });

        await PushAsync(id);

        (await Notify(db => db.PushDevices.IgnoreQueryFilters().Where(d => d.DeviceId == "device-dead-001").Select(d => d.RevokedReason).SingleAsync())).ShouldBe("device_not_registered");
        factory.Gateway.Sent.ShouldContain(m => m.To == alive);
        (await client.DeleteAsync("/api/v1/devices/device-alive-01")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.DeleteAsync("/api/v1/devices/device-alive-01")).StatusCode.ShouldBe(HttpStatusCode.NoContent); // idempotent
        (await Notify(db => db.PushDevices.IgnoreQueryFilters().Where(d => d.DeviceId == "device-alive-01").Select(d => d.RevokedReason).SingleAsync())).ShouldBe("unregistered");
    }

    [Fact]
    public async Task Registration_upserts_validates_and_moves_a_token_between_accounts()
    {
        var first = await TestUsers.SignedInClientAsync(factory);
        var second = await TestUsers.SignedInClientAsync(factory);
        var token = Token();

        (await RegisterAsync(first, "device-move-001", token)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await RegisterAsync(first, "device-move-001", token)).StatusCode.ShouldBe(HttpStatusCode.Created); // upsert
        (await RegisterAsync(second, "device-move-002", token)).StatusCode.ShouldBe(HttpStatusCode.Created); // the phone changed hands

        var rows = await Notify(db => db.PushDevices.IgnoreQueryFilters().Where(d => d.DeviceId == "device-move-001" || d.DeviceId == "device-move-002").ToListAsync());
        rows.Count.ShouldBe(2);
        rows.Single(d => d.DeviceId == "device-move-001").RevokedReason.ShouldBe("token_moved");
        rows.Single(d => d.DeviceId == "device-move-002").PushToken.ShouldBe(token);
        (await second.DeleteAsync("/api/v1/devices/device-move-001")).StatusCode.ShouldBe(HttpStatusCode.NoContent); // another user's id matches nothing

        (await RegisterAsync(first, "device-bad-0001", "not-a-token")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await RegisterAsync(first, "device-bad-0001", Token(), "windows")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await RegisterAsync(first, "", Token())).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await factory.CreateClient().PostAsJsonAsync("/api/v1/devices", new { deviceId = "x", pushToken = Token(), platform = "ios" })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("order_filled", "fills")]
    [InlineData("stop_hit", "fills")]
    [InlineData("stop_proximity", "stopProximity")]
    [InlineData("analysis_ready", "signals")]
    [InlineData("strategy_guard", "strategyGuard")]
    [InlineData("paper_reset", "system")]
    public void Each_item_type_maps_to_its_preference(string type, string preference)
    {
        var off = new Astryx.Contracts.Profiles.PushPreferences(
            preference != "fills", preference != "signals", preference != "stopProximity", preference != "strategyGuard", preference != "system", true, true);

        PushSender.Wants(off, type).ShouldBeFalse();
        PushSender.Wants(off with { Fills = true, Signals = true, StopProximity = true, StrategyGuard = true, System = true }, type).ShouldBeTrue();
    }
}
