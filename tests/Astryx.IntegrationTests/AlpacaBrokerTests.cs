using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Astryx.Infrastructure.Alpaca;
using Astryx.Infrastructure.Outbox;
using Astryx.Infrastructure.Security;
using Astryx.Modules.Paper.Application;
using Astryx.Modules.Paper.Infrastructure.Alpaca;
using Astryx.Modules.Paper.Infrastructure.Persistence;
using Astryx.Modules.Portfolio.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.IntegrationTests;

/// <summary>
/// An in-memory Alpaca paper account (SYSTEM_DESIGN.md §17.3): accepts one key pair, records brackets, and lets a test
/// decide when Alpaca fills, rejects or loses a response.
/// </summary>
public sealed class FakeAlpaca : IAlpacaTradingClient
{
    public const string KeyId = "PKTESTKEY0003F2A";
    public const string Secret = "fake-secret-never-logged";

    public decimal Cash { get; set; } = 99_975.24m;

    public ConcurrentDictionary<string, AlpacaOrder> Orders { get; } = new();

    public ConcurrentQueue<AlpacaBracketRequest> Submitted { get; } = new();

    /// <summary>The next submit stores the order but throws as if the response was lost.</summary>
    public bool LoseNextResponse { get; set; }

    public AlpacaApiException? RejectNext { get; set; }

    /// <summary>The next lookup by client id misses, as if Alpaca had not indexed the order yet.</summary>
    public bool MissNextLookup { get; set; }

    public List<string> Cancelled { get; } = [];

    private static void Check(AlpacaCredentials credentials)
    {
        if (credentials.KeyId != KeyId || credentials.SecretKey != Secret)
        {
            throw new AlpacaApiException(AlpacaFailure.CredentialsInvalid, 401, "Alpaca 401: unauthorized.");
        }
    }

    public Task<AlpacaAccount> GetAccountAsync(AlpacaCredentials credentials, CancellationToken cancellationToken)
    {
        Check(credentials);
        return Task.FromResult(new AlpacaAccount("PA3TESTACCT", "ACTIVE", Cash, Cash, Cash * 2, false, false));
    }

    public Task<AlpacaAsset?> GetAssetAsync(AlpacaCredentials credentials, string symbol, CancellationToken cancellationToken) =>
        Task.FromResult<AlpacaAsset?>(new AlpacaAsset(symbol, "active", true, true));

    public Task<AlpacaOrder> SubmitBracketAsync(AlpacaCredentials credentials, AlpacaBracketRequest request, CancellationToken cancellationToken)
    {
        Check(credentials);
        if (RejectNext is { } reject)
        {
            RejectNext = null;
            throw reject;
        }

        if (Orders.Values.Any(o => o.ClientOrderId == request.ClientOrderId))
        {
            throw new AlpacaApiException(AlpacaFailure.Invalid, 422, "Alpaca 422: client_order_id must be unique"); // as Paper answers (Q-22)
        }

        Submitted.Enqueue(request);
        var id = Guid.NewGuid().ToString();
        var order = new AlpacaOrder(id, request.ClientOrderId, "accepted", request.Symbol, "buy", request.Type, request.Quantity, 0m, null, null, request.StopPrice, request.LimitPrice, "bracket",
        [
            new AlpacaOrder($"{id}-tp", $"{id}-tp", "held", request.Symbol, "sell", "limit", request.Quantity, 0m, null, null, null, request.TakeProfit, "bracket", []),
            new AlpacaOrder($"{id}-sl", $"{id}-sl", "held", request.Symbol, "sell", "stop", request.Quantity, 0m, null, null, request.StopLoss, null, "bracket", []),
        ]);
        Orders[id] = order;
        if (LoseNextResponse)
        {
            LoseNextResponse = false;
            throw new AlpacaApiException(AlpacaFailure.Unavailable, null, "Alpaca timed out.");
        }

        return Task.FromResult(order);
    }

    public Task<AlpacaOrder?> GetOrderAsync(AlpacaCredentials credentials, string orderId, CancellationToken cancellationToken)
    {
        Check(credentials);
        var found = Orders.TryGetValue(orderId, out var order) ? order : Orders.Values.SelectMany(o => o.Legs).FirstOrDefault(l => l.Id == orderId);
        return Task.FromResult(found);
    }

    public Task<AlpacaOrder?> GetOrderByClientIdAsync(AlpacaCredentials credentials, string clientOrderId, CancellationToken cancellationToken)
    {
        Check(credentials);
        if (MissNextLookup)
        {
            MissNextLookup = false;
            return Task.FromResult<AlpacaOrder?>(null);
        }

        return Task.FromResult(Orders.Values.FirstOrDefault(o => o.ClientOrderId == clientOrderId));
    }

    public Task CancelOrderAsync(AlpacaCredentials credentials, string orderId, CancellationToken cancellationToken)
    {
        Check(credentials);
        lock (Cancelled)
        {
            Cancelled.Add(orderId);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AlpacaPosition>> ListPositionsAsync(AlpacaCredentials credentials, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AlpacaPosition>>([]);

    public Task<AlpacaOrder> ClosePositionAsync(AlpacaCredentials credentials, string symbol, CancellationToken cancellationToken)
    {
        Check(credentials);
        var id = Guid.NewGuid().ToString();
        var order = new AlpacaOrder(id, id, "filled", symbol, "sell", "market", 64m, 64m, 190.10m, DateTimeOffset.UtcNow, null, null, "simple", []);
        Orders[id] = order;
        return Task.FromResult(order);
    }

    /// <summary>Alpaca fills the entry at <paramref name="price"/>; the bracket legs become live.</summary>
    public void FillEntry(string clientOrderId, decimal price)
    {
        var order = Orders.Values.Single(o => o.ClientOrderId == clientOrderId);
        Orders[order.Id] = order with
        {
            Status = "filled",
            FilledQuantity = order.Quantity,
            FilledAvgPrice = price,
            FilledAt = DateTimeOffset.UtcNow,
            Legs = order.Legs.Select(l => l with { Status = "new" }).ToList(),
        };
    }

    /// <summary>Alpaca fills one bracket leg (the other is cancelled by Alpaca's OCO).</summary>
    public void FillLeg(string legId, decimal price)
    {
        var parent = Orders.Values.Single(o => o.Legs.Any(l => l.Id == legId));
        Orders[parent.Id] = parent with
        {
            Legs = parent.Legs.Select(l => l.Id == legId
                ? l with { Status = "filled", FilledQuantity = l.Quantity, FilledAvgPrice = price, FilledAt = DateTimeOffset.UtcNow }
                : l with { Status = "canceled" }).ToList(),
        };
    }

    public void SetStatus(string clientOrderId, string status)
    {
        var order = Orders.Values.Single(o => o.ClientOrderId == clientOrderId);
        Orders[order.Id] = order with { Status = status };
    }
}

public sealed class AlpacaApiFactory : TradingApiFactory
{
    public FakeAlpaca Alpaca { get; } = new();

    protected override IReadOnlyDictionary<string, string> ExtraSettings => new Dictionary<string, string>(base.ExtraSettings)
    {
        ["Credentials:MasterKeys"] = CredentialVault.GenerateKey(),
        ["Workers:Jobs"] = "false", // the tests drive the broker jobs; the outbox still delivers sign-up email
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.AddSingleton<IAlpacaTradingClient>(Alpaca));
    }

    public AlpacaExecution Execution => Services.GetRequiredService<AlpacaExecution>();
}

/// <summary>Connecting Alpaca and trading through it (SYSTEM_DESIGN.md §17, TASKS.md T-140–T-145).</summary>
public class AlpacaBrokerTests(AlpacaApiFactory factory) : IClassFixture<AlpacaApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await factory.PrepareAsync();
        await factory.ScanAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static Task<HttpResponseMessage> ConnectAsync(HttpClient client, string keyId = FakeAlpaca.KeyId, string secret = FakeAlpaca.Secret, string environment = "paper") =>
        client.PostAsJsonAsync("/api/v1/broker/connections", new { broker = "alpaca", environment, keyId, secretKey = secret });

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object? body = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body ?? new { }) };
        request.Headers.Add("Idempotency-Key", $"key-{Guid.NewGuid():N}");
        return client.SendAsync(request);
    }

    private async Task<(HttpClient Client, JsonElement Order)> ConnectAndPlaceAsync()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        (await ConnectAsync(client)).StatusCode.ShouldBe(HttpStatusCode.Created);
        await factory.MakeQuoteLiveAsync("NVDA", 186.60m);
        var proposal = await (await client.PostAsJsonAsync("/api/v1/proposals", new { source = "signal", signalId = await factory.ActiveSignalIdAsync("NVDA") })).Content.ReadFromJsonAsync<JsonElement>();
        var response = await PostAsync(client, "/api/v1/paper/orders", new { proposalId = proposal.GetProperty("id").GetGuid(), assessmentId = proposal.GetProperty("assessment").GetProperty("id").GetGuid() });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (client, await response.Content.ReadFromJsonAsync<JsonElement>());
    }

    private async Task<T> Query<TContext, T>(Func<TContext, Task<T>> query)
        where TContext : notnull
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<TContext>());
    }

    [Fact]
    public async Task Connecting_validates_the_keys_seals_the_secret_and_switches_trading_to_the_Alpaca_portfolio()
    {
        var client = await TestUsers.SignedInClientAsync(factory);

        (await ConnectAsync(client, secret: "wrong")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ConnectAsync(client, environment: "live")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var connected = await ConnectAsync(client);

        connected.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await connected.Content.ReadFromJsonAsync<JsonElement>();
        (body.GetProperty("label").GetString(), body.GetProperty("keySuffix").GetString(), body.GetProperty("accountRef").GetString()).ShouldBe(("Alpaca · paper account", "3F2A", "PA3TESTACCT"));
        body.GetProperty("permissions").GetProperty("depositsWithdrawals").GetString().ShouldBe("never_requested");
        (await connected.Content.ReadAsStringAsync()).ShouldNotContain(FakeAlpaca.Secret);
        var stored = await Query<PaperDbContext, byte[]>(db => db.BrokerConnections.IgnoreQueryFilters().Where(c => c.Id == body.GetProperty("id").GetGuid()).Select(c => c.CredentialsCiphertext).SingleAsync());
        Encoding.UTF8.GetString(stored).ShouldNotContain(FakeAlpaca.Secret); // sealed, not plaintext
        (await ConnectAsync(client)).StatusCode.ShouldBe(HttpStatusCode.Conflict); // one paper connection

        var portfolio = await client.GetFromJsonAsync<JsonElement>("/api/v1/portfolio");
        portfolio.GetProperty("cash").GetString().ShouldBe("99975.24"); // Alpaca's balance, not the simulated $100,000

        (await client.DeleteAsync($"/api/v1/broker/connections/{body.GetProperty("id").GetGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Query<PaperDbContext, byte[]>(db => db.BrokerConnections.IgnoreQueryFilters().Where(c => c.Id == body.GetProperty("id").GetGuid()).Select(c => c.CredentialsCiphertext).SingleAsync())).ShouldBeEmpty();
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/broker/connections")).GetProperty("items").GetArrayLength().ShouldBe(0);
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/portfolio")).GetProperty("cash").GetString().ShouldNotBe("99975.24"); // back to the simulated account
    }

    [Fact]
    public async Task Reconnecting_to_an_account_with_another_balance_starts_a_new_epoch_not_a_profit()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        var first = await (await ConnectAsync(client)).Content.ReadFromJsonAsync<JsonElement>();
        (await client.DeleteAsync($"/api/v1/broker/connections/{first.GetProperty("id").GetGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var before = factory.Alpaca.Cash;
        factory.Alpaca.Cash = 100_000m; // new keys, a different paper account
        try
        {
            (await ConnectAsync(client)).StatusCode.ShouldBe(HttpStatusCode.Created);

            var portfolio = await client.GetFromJsonAsync<JsonElement>("/api/v1/portfolio");
            (portfolio.GetProperty("cash").GetString(), portfolio.GetProperty("allTimePnl").GetString(), portfolio.GetProperty("dayPnl").GetString())
                .ShouldBe(("100000.00", "0.00", "0.00"));
        }
        finally
        {
            factory.Alpaca.Cash = before;
        }
    }

    [Fact]
    public async Task An_approved_order_goes_to_Alpaca_as_a_bracket_and_mirrors_Alpacas_fill()
    {
        var (client, order) = await ConnectAndPlaceAsync();
        var orderId = order.GetProperty("id").GetGuid();
        (order.GetProperty("status").GetString(), order.GetProperty("broker").GetString()).ShouldBe(("submitting", "alpaca"));

        await factory.Execution.SubmitAsync(orderId, default);

        var sent = factory.Alpaca.Submitted.Single(s => s.ClientOrderId == orderId.ToString());
        (sent.Symbol, sent.Type, sent.Quantity, sent.StopPrice, sent.StopLoss, sent.TakeProfit).ShouldBe(("NVDA", "stop", 64m, 186.50m, 178.20m, 204.00m));
        (await client.GetFromJsonAsync<JsonElement>($"/api/v1/paper/orders/{orderId}")).GetProperty("status").GetString().ShouldBe("submitted");

        (await factory.Execution.SettleAsync(orderId, default)).ShouldBeFalse(); // Alpaca accepted it; the buy stop rests
        (await client.GetFromJsonAsync<JsonElement>($"/api/v1/paper/orders/{orderId}")).GetProperty("status").GetString().ShouldBe("working");

        factory.Alpaca.FillEntry(orderId.ToString(), 186.71m);
        (await factory.Execution.SettleAsync(orderId, default)).ShouldBeTrue();

        var filled = await client.GetFromJsonAsync<JsonElement>($"/api/v1/paper/orders/{orderId}");
        (filled.GetProperty("status").GetString(), filled.GetProperty("fill").GetProperty("price").GetString()).ShouldBe(("filled", "186.71")); // Alpaca's price, not a simulated one
        filled.GetProperty("attached").GetProperty("stop").GetString().ShouldBe("178.20");
        var legs = await Query<PaperDbContext, List<string>>(db => db.Orders.IgnoreQueryFilters().Where(o => o.PositionId == filled.GetProperty("positionId").GetGuid() && o.OcoGroupId != null).Select(o => o.Broker + ":" + o.BrokerOrderId).ToListAsync());
        legs.ShouldAllBe(l => l.StartsWith("alpaca:") && (l.EndsWith("-sl") || l.EndsWith("-tp"))); // the exits mirror Alpaca's bracket legs
        (await factory.Execution.SettleAsync(orderId, default)).ShouldBeTrue(); // settling again records nothing twice
        (await Query<PaperDbContext, int>(db => db.Fills.IgnoreQueryFilters().CountAsync(f => f.OrderId == orderId))).ShouldBe(1);
    }

    [Fact]
    public async Task Alpaca_filling_the_stop_leg_closes_the_position_at_Alpacas_price()
    {
        var (client, order) = await ConnectAndPlaceAsync();
        var orderId = order.GetProperty("id").GetGuid();
        await factory.Execution.SubmitAsync(orderId, default);
        factory.Alpaca.FillEntry(orderId.ToString(), 186.60m);
        await factory.Execution.SettleAsync(orderId, default);
        var positionId = (await client.GetFromJsonAsync<JsonElement>($"/api/v1/paper/orders/{orderId}")).GetProperty("positionId").GetGuid();
        var stopLeg = await Query<PaperDbContext, string>(db => db.Orders.IgnoreQueryFilters().Where(o => o.PositionId == positionId && o.Purpose == "exit_stop").Select(o => o.BrokerOrderId).SingleAsync());

        await factory.MakeQuoteLiveAsync("NVDA", 150m); // far below the stop: the simulator must not act on an Alpaca position
        await factory.Execution.SyncAsync(default);
        (await Query<PortfolioDbContext, string>(db => db.Positions.IgnoreQueryFilters().Where(p => p.Id == positionId).Select(p => p.Status).SingleAsync())).ShouldBe("open");

        factory.Alpaca.FillLeg(stopLeg, 177.95m);
        await factory.Execution.SyncAsync(default);

        var position = await Query<PortfolioDbContext, Astryx.Modules.Portfolio.Domain.Position>(db => db.Positions.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == positionId));
        (position.Status, position.ExitReason, position.ExitPrice).ShouldBe(("closed", "stop", 177.95m));
    }

    [Fact]
    public async Task A_rejected_order_releases_its_reservation()
    {
        var (client, order) = await ConnectAndPlaceAsync();
        var orderId = order.GetProperty("id").GetGuid();
        var held = await client.GetFromJsonAsync<JsonElement>("/api/v1/portfolio");
        held.GetProperty("cashAvailable").GetString().ShouldNotBe(held.GetProperty("cash").GetString()); // the worst-case cost is reserved
        factory.Alpaca.RejectNext = new AlpacaApiException(AlpacaFailure.InsufficientFunds, 403, "Alpaca 403: insufficient buying power");

        await factory.Execution.SubmitAsync(orderId, default);

        var rejected = await client.GetFromJsonAsync<JsonElement>($"/api/v1/paper/orders/{orderId}");
        rejected.GetProperty("status").GetString().ShouldBe("rejected");
        var after = await client.GetFromJsonAsync<JsonElement>("/api/v1/portfolio");
        after.GetProperty("cashAvailable").GetString().ShouldBe(after.GetProperty("cash").GetString());
        var proposalId = rejected.GetProperty("proposalId").GetGuid();
        await Eventually(async () => (await client.GetFromJsonAsync<JsonElement>($"/api/v1/proposals/{proposalId}")).GetProperty("status").GetString() == "pending",
            "the proposal back to pending"); // never executed: the user can retry or reject it
    }

    private async Task Eventually(Func<Task<bool>> condition, string what)
    {
        var dispatcher = factory.Services.GetRequiredService<OutboxDispatcher>();
        for (var deadline = DateTime.UtcNow.AddSeconds(15); DateTime.UtcNow < deadline; await Task.Delay(100))
        {
            await dispatcher.DispatchOnceAsync(default);
            if (await condition())
            {
                return;
            }
        }

        throw new TimeoutException($"Timed out waiting for {what}.");
    }

    [Fact]
    public async Task Ten_concurrent_approvals_with_one_key_send_one_order_to_Alpaca()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        (await ConnectAsync(client)).StatusCode.ShouldBe(HttpStatusCode.Created);
        await factory.MakeQuoteLiveAsync("NVDA", 186.60m);
        var proposal = await (await client.PostAsJsonAsync("/api/v1/proposals", new { source = "signal", signalId = await factory.ActiveSignalIdAsync("NVDA") })).Content.ReadFromJsonAsync<JsonElement>();
        var body = new { proposalId = proposal.GetProperty("id").GetGuid(), assessmentId = proposal.GetProperty("assessment").GetProperty("id").GetGuid() };
        var key = $"key-{Guid.NewGuid():N}";

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/paper/orders") { Content = JsonContent.Create(body) };
            request.Headers.Add("Idempotency-Key", key);
            return client.SendAsync(request);
        }));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created || r.StatusCode == HttpStatusCode.Conflict); // replays, or "still in progress"
        var orderIds = new HashSet<Guid>();
        foreach (var response in responses.Where(r => r.StatusCode == HttpStatusCode.Created))
        {
            orderIds.Add((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        }

        var orderId = orderIds.ShouldHaveSingleItem();
        var before = factory.Alpaca.Submitted.Count;
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => factory.Execution.SubmitAsync(orderId, default))); // the job, retried while in flight
        (factory.Alpaca.Submitted.Count - before).ShouldBe(1);
        (await client.GetFromJsonAsync<JsonElement>($"/api/v1/paper/orders/{orderId}")).GetProperty("status").GetString().ShouldNotBe("rejected");
    }

    [Fact]
    public async Task An_entry_unresolved_after_ten_minutes_counts_as_stuck()
    {
        var (_, order) = await ConnectAndPlaceAsync();
        var orderId = order.GetProperty("id").GetGuid();
        var before = await factory.Execution.ReportStuckAsync(default);

        await Query<PaperDbContext, int>(db => db.Orders.IgnoreQueryFilters().Where(o => o.Id == orderId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.SubmittedAt, o => o.SubmittedAt - TimeSpan.FromMinutes(11L))));

        (await factory.Execution.ReportStuckAsync(default)).ShouldBe(before + 1);
    }

    [Fact]
    public async Task A_lost_submit_response_is_found_by_client_order_id_instead_of_resent()
    {
        var (_, order) = await ConnectAndPlaceAsync();
        var orderId = order.GetProperty("id").GetGuid();
        factory.Alpaca.LoseNextResponse = true;

        await Should.ThrowAsync<AlpacaApiException>(() => factory.Execution.SubmitAsync(orderId, default)); // the job runner retries
        await factory.Execution.SubmitAsync(orderId, default);

        factory.Alpaca.Submitted.Count(s => s.ClientOrderId == orderId.ToString()).ShouldBe(1);
    }

    [Fact]
    public async Task A_duplicate_client_order_id_answer_continues_with_the_order_Alpaca_already_holds()
    {
        var (client, order) = await ConnectAndPlaceAsync();
        var orderId = order.GetProperty("id").GetGuid();
        await factory.Execution.SubmitAsync(orderId, default); // Alpaca holds it
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PaperDbContext>();
            var row = await db.Orders.IgnoreQueryFilters().SingleAsync(o => o.Id == orderId);
            row.Status = "submitting"; // as if the first attempt's response was lost before it was recorded
            await db.SaveChangesAsync();
        }

        factory.Alpaca.MissNextLookup = true; // so the retry resubmits and meets Alpaca's duplicate check
        factory.Alpaca.RejectNext = new AlpacaApiException(AlpacaFailure.Invalid, 422, "Alpaca 422: client_order_id must be unique");
        await factory.Execution.SubmitAsync(orderId, default);

        (await client.GetFromJsonAsync<JsonElement>($"/api/v1/paper/orders/{orderId}")).GetProperty("status").GetString().ShouldBe("submitted");
    }

    [Fact]
    public async Task Sync_posts_an_adjustment_when_Alpacas_cash_moved_outside_the_app()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        (await ConnectAsync(client)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var before = factory.Alpaca.Cash;
        factory.Alpaca.Cash = before + 250m;
        try
        {
            await factory.Execution.SyncAsync(default);

            (await client.GetFromJsonAsync<JsonElement>("/api/v1/portfolio")).GetProperty("cash").GetString().ShouldBe((before + 250m).ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
        }
        finally
        {
            factory.Alpaca.Cash = before;
        }
    }
}
