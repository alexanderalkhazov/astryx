using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Astryx.Infrastructure.Outbox;
using Astryx.Infrastructure.Realtime;
using Astryx.Modules.Paper.Application;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.IntegrationTests;

/// <summary><c>order.updated</c> and <c>position.updated</c> over the hub (SYSTEM_DESIGN.md §9.6, TASKS.md T-089).</summary>
public class PaperRealtimeTests(TradingApiFactory factory) : IClassFixture<TradingApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await factory.PrepareAsync();
        await factory.ScanAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_filled_entry_pushes_order_and_position_updates_to_its_owner()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        var orders = new ConcurrentQueue<OrderUpdatedMessage>();
        var positions = new ConcurrentQueue<PositionUpdatedMessage>();
        await using var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, WorkstationHub.Path), o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
                o.AccessTokenProvider = () => Task.FromResult<string?>(client.DefaultRequestHeaders.Authorization!.Parameter);
            })
            .Build();
        hub.On<OrderUpdatedMessage>(PaperRealtimeRelays.OrderUpdated, orders.Enqueue);
        hub.On<PositionUpdatedMessage>(PaperRealtimeRelays.PositionUpdated, positions.Enqueue);
        await hub.StartAsync();

        await factory.MakeQuoteLiveAsync("NVDA", 186.60m);
        var proposal = await (await client.PostAsJsonAsync("/api/v1/proposals", new { source = "signal", signalId = await factory.ActiveSignalIdAsync("NVDA") })).Content.ReadFromJsonAsync<JsonElement>();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/paper/orders")
        {
            Content = JsonContent.Create(new { proposalId = proposal.GetProperty("id").GetGuid(), assessmentId = proposal.GetProperty("assessment").GetProperty("id").GetGuid() }),
        };
        request.Headers.Add("Idempotency-Key", $"order-{Guid.NewGuid():N}");
        var order = await (await client.SendAsync(request)).Content.ReadFromJsonAsync<JsonElement>();
        var dispatcher = factory.Services.GetRequiredService<OutboxDispatcher>();
        while (await dispatcher.DispatchOnceAsync(default) > 0)
        {
        }

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while ((orders.Count < 2 || positions.IsEmpty) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        var orderId = order.GetProperty("id").GetGuid();
        orders.Where(o => o.OrderId == orderId).Select(o => o.Status).Order().ShouldBe(["filled", "filled"]); // accepted (as filled) and the fill itself
        var fill = orders.Single(o => o.OrderId == orderId && o.Price is not null);
        (fill.Symbol, fill.Purpose, fill.Quantity, fill.Price).ShouldBe(("NVDA", "entry", 64m, 186.62m));
        positions.Single().ShouldSatisfyAllConditions(p => p.Status.ShouldBe("open"), p => p.Symbol.ShouldBe("NVDA"));
    }
}
