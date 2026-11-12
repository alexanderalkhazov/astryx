using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astryx.Infrastructure.Outbox;
using Astryx.Infrastructure.Realtime;
using Astryx.Modules.Ai.Application.Jobs;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.IntegrationTests;

/// <summary>The SignalR hub and <c>analysis.updated</c> via the outbox (SYSTEM_DESIGN.md §9.6, TASKS.md T-074).</summary>
public class RealtimeTests(FakeAiApiFactory factory) : IClassFixture<FakeAiApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await AnalysisSteps.PrepareAsync(factory);
        await AnalysisSteps.DrainAsync(factory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HubConnection Connect(string? token) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, WorkstationHub.Path), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling; // TestServer has no WebSocket upgrade for the client
                options.AccessTokenProvider = () => Task.FromResult(token);
            })
            .Build();

    private static string TokenOf(HttpClient client) => client.DefaultRequestHeaders.Authorization!.Parameter!;

    private async Task DispatchOutboxAsync()
    {
        var dispatcher = factory.Services.GetRequiredService<OutboxDispatcher>();
        while (await dispatcher.DispatchOnceAsync(default) > 0)
        {
        }
    }

    [Fact]
    public async Task The_hub_requires_a_token_and_accepts_it_in_the_query_string()
    {
        var client = factory.CreateClient();
        var user = await TestUsers.SignedInClientAsync(factory);

        (await client.PostAsync($"{WorkstationHub.Path}/negotiate?negotiateVersion=1", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostAsync($"{WorkstationHub.Path}/negotiate?negotiateVersion=1&access_token={TokenOf(user)}", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await Should.ThrowAsync<HttpRequestException>(() => Connect(null).StartAsync());
    }

    [Fact]
    public async Task Job_progress_reaches_only_the_owners_connections()
    {
        var alice = await TestUsers.SignedInClientAsync(factory);
        var bob = await TestUsers.SignedInClientAsync(factory);
        var aliceEvents = new ConcurrentQueue<AnalysisUpdatedMessage>();
        var bobEvents = new ConcurrentQueue<AnalysisUpdatedMessage>();
        await using var aliceHub = Connect(TokenOf(alice));
        await using var bobHub = Connect(TokenOf(bob));
        aliceHub.On<AnalysisUpdatedMessage>(AnalysisUpdatedHandler.EventName, aliceEvents.Enqueue);
        bobHub.On<AnalysisUpdatedMessage>(AnalysisUpdatedHandler.EventName, bobEvents.Enqueue);
        await aliceHub.StartAsync();
        await bobHub.StartAsync();

        var job = await (await AnalysisSteps.RequestAsync(alice)).Content.ReadFromJsonAsync<JsonElement>();
        var jobId = job.GetProperty("id").GetGuid();
        await AnalysisSteps.DrainAsync(factory);
        await DispatchOutboxAsync();

        await WaitUntilAsync(() => aliceEvents.Any(e => e.JobId == jobId && e.Status == "completed"));
        var events = aliceEvents.Where(e => e.JobId == jobId).ToList();
        events.Select(e => e.Status).ShouldBe(["processing", "completed"]);
        events[1].ResultId.ShouldNotBeNull();
        events[1].Symbol.ShouldBe("NVDA");
        bobEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancelling_a_pending_job_is_announced()
    {
        var alice = await TestUsers.SignedInClientAsync(factory);
        var events = new ConcurrentQueue<AnalysisUpdatedMessage>();
        await using var hub = Connect(TokenOf(alice));
        hub.On<AnalysisUpdatedMessage>(AnalysisUpdatedHandler.EventName, events.Enqueue);
        await hub.StartAsync();
        var jobId = (await (await AnalysisSteps.RequestAsync(alice)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await alice.DeleteAsync($"/api/v1/analyses/jobs/{jobId}");
        await DispatchOutboxAsync();

        await WaitUntilAsync(() => events.Any(e => e.JobId == jobId));
        events.Single(e => e.JobId == jobId).ShouldSatisfyAllConditions(e => e.Status.ShouldBe("cancelled"), e => e.ErrorCode.ShouldBe("cancelled"));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The expected realtime event did not arrive.");
            }

            await Task.Delay(50);
        }
    }
}
