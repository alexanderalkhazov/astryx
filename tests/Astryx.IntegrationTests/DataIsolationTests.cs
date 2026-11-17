using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Astryx.Infrastructure.Security;
using Astryx.Modules.Ai.Domain;
using Astryx.Modules.Ai.Infrastructure.Persistence;
using Astryx.Modules.Identity.Infrastructure.Persistence;
using Astryx.Modules.Paper.Domain;
using Astryx.Modules.Paper.Infrastructure.Persistence;
using Astryx.Modules.Portfolio.Infrastructure.Persistence;
using Astryx.Modules.Profiles.Infrastructure.Persistence;
using Astryx.Modules.Trading.Domain;
using Astryx.Modules.Trading.Infrastructure.Persistence;
using Astryx.Modules.Watchlists.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.IntegrationTests;

/// <summary>Per-user data isolation (T-024, SYSTEM_DESIGN.md §18.5, §21.6).</summary>
public partial class DataIsolationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    /// <summary>
    /// Routes that may be called without a token. Everything else under <c>/api</c> must require authentication.
    /// Keep this list short and deliberate.
    /// </summary>
    private static readonly HashSet<string> AnonymousRoutes = new(StringComparer.Ordinal)
    {
        "POST /api/v1/auth/register",
        "POST /api/v1/auth/verify-email",
        "POST /api/v1/auth/resend-verification",
        "POST /api/v1/auth/login",
        "POST /api/v1/auth/refresh",
        "POST /api/v1/auth/forgot-password",
        "POST /api/v1/auth/reset-password",
        "GET /api/v1/system/status",
    };

    /// <summary>
    /// Isolation fixtures: for every authenticated route with a route parameter, a check that user B cannot reach
    /// user A's resource (expects 404). A new parameterised route fails <see cref="Every_parameterised_route_has_an_isolation_fixture"/>
    /// until an entry is added here.
    /// </summary>
    private static readonly Dictionary<string, Func<DataIsolationTests, Task>> IsolationFixtures = new(StringComparer.Ordinal)
    {
        ["PATCH /api/v1/watchlists/{id}"] = t => t.AssertForeignWatchlistIs404Async(HttpMethod.Patch, "", new { name = "hijacked" }),
        ["DELETE /api/v1/watchlists/{id}"] = t => t.AssertForeignWatchlistIs404Async(HttpMethod.Delete, "", null),
        ["POST /api/v1/watchlists/{id}/items"] = t => t.AssertForeignWatchlistIs404Async(HttpMethod.Post, "/items", new { symbol = "NVDA" }),
        ["DELETE /api/v1/watchlists/{id}/items/{symbol}"] = t => t.AssertForeignWatchlistIs404Async(HttpMethod.Delete, "/items/NVDA", null),
        ["PUT /api/v1/watchlists/{id}/order"] = t => t.AssertForeignWatchlistIs404Async(HttpMethod.Put, "/order", new { symbols = Array.Empty<string>() }),
        ["PUT /api/v1/watchlists/default/items/{symbol}"] = t => t.AssertStarOnlyTouchesOwnDefaultAsync(),
        ["GET /api/v1/analyses/jobs/{jobId:guid}"] = t => t.AssertForeignAnalysisIs404Async(HttpMethod.Get, a => $"/api/v1/analyses/jobs/{a.JobId}"),
        ["DELETE /api/v1/analyses/jobs/{jobId:guid}"] = t => t.AssertForeignAnalysisIs404Async(HttpMethod.Delete, a => $"/api/v1/analyses/jobs/{a.JobId}"),
        ["GET /api/v1/analyses/{id:guid}"] = t => t.AssertForeignAnalysisIs404Async(HttpMethod.Get, a => $"/api/v1/analyses/{a.ResultId}"),
        ["GET /api/v1/proposals/{id:guid}"] = t => t.AssertForeignProposalIs404Async(HttpMethod.Get, ""),
        ["POST /api/v1/proposals/{id:guid}/reject"] = t => t.AssertForeignProposalIs404Async(HttpMethod.Post, "/reject"),
        ["GET /api/v1/paper/orders/{id:guid}"] = t => t.AssertForeignOrderIs404Async(),
        // Positions and order cancellation are covered with real fills in ClosePositionTests.Another_users_position_and_order_are_not_found.
        ["POST /api/v1/paper/orders/{id:guid}/cancel"] = t => t.AssertForeignOrderIs404Async(cancel: true),
        ["POST /api/v1/positions/{id:guid}/close-preview"] = t => t.AssertUnknownPositionIs404Async("/close-preview"),
        ["POST /api/v1/positions/{id:guid}/close"] = t => t.AssertUnknownPositionIs404Async("/close"),
        // Detail reads with a real foreign position are in PortfolioReadTests.Position_detail_has_facts_the_level_track_and_lineage_and_clears_NEW.
        // With a real foreign notification in NotificationTests.IDEM_06_a_redelivered_event_creates_one_notification_and_read_all_clears_them.
        ["POST /api/v1/notifications/{id:guid}/read"] = t => t.AssertUnknownNotificationIs404Async(),
        ["GET /api/v1/positions/{id:guid}"] = t => t.AssertUnknownPositionReadIs404Async($"/api/v1/positions/{Guid.NewGuid()}"),
        ["GET /api/v1/positions/by-symbol/{symbol}"] = t => t.AssertUnknownPositionReadIs404Async("/api/v1/positions/by-symbol/NVDA"),
        ["POST /api/v1/proposals/{id:guid}/assessments"] = t => t.AssertForeignProposalIs404Async(HttpMethod.Post, "/assessments", new { riskPerTradePct = "0.0075" }),
        // N5 is idempotent (204 either way, so it reveals nothing); isolation means Alice's device keeps its pushes.
        ["DELETE /api/v1/devices/{deviceId}"] = t => t.AssertForeignDeviceIsUntouchedAsync(),
        ["DELETE /api/v1/broker/connections/{id:guid}"] = t => t.AssertForeignBrokerConnectionIs404Async(),
    };

    /// <summary>
    /// Parameterised routes over shared reference data (market data keyed by symbol): no user owns the resource, so
    /// there is nothing to isolate. Each entry is a deliberate exemption; user-owned routes never belong here.
    /// </summary>
    private static readonly HashSet<string> SharedDataRoutes = new(StringComparer.Ordinal)
    {
        "GET /api/v1/instruments/{symbol}",
        "GET /api/v1/instruments/{symbol}/fundamentals",
        "GET /api/v1/instruments/{symbol}/news",
        "GET /api/v1/market/{symbol}/quote",
        "GET /api/v1/market/{symbol}/bars",
        "GET /api/v1/market/{symbol}/indicators",
        // The system strategy catalog: no owner (per-user strategies are Q-10).
        "GET /api/v1/strategies/{id}",
        "GET /api/v1/strategies/{id}/runs/{runId}",
        // Scanner output is global, the same for every user (§10.4 discovery.signals).
        "GET /api/v1/signals/{id:guid}",
    };

    /// <summary>Bob addresses Alice's list by id (with Alice's current ETag): 404, and Alice's list is unchanged.</summary>
    private async Task AssertForeignWatchlistIs404Async(HttpMethod method, string suffix, object? body)
    {
        var alice = await TestUsers.SignedInClientAsync(factory);
        var bob = await TestUsers.SignedInClientAsync(factory);
        var created = await (await alice.PostAsJsonAsync("/api/v1/watchlists", new { name = "Mine" })).Content.ReadFromJsonAsync<JsonElement>();
        var request = new HttpRequestMessage(method, $"/api/v1/watchlists/{created.GetProperty("id").GetString()}{suffix}");
        request.Headers.TryAddWithoutValidation("If-Match", "\"v1\"");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        var response = await bob.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, $"{method} {suffix}");
        var mine = (await alice.GetFromJsonAsync<JsonElement>("/api/v1/watchlists")).GetProperty("items").EnumerateArray().Single(l => l.GetProperty("name").GetString() == "Mine");
        mine.GetProperty("version").GetInt32().ShouldBe(1);
    }

    /// <summary>The star route has no list id: it always writes the caller's own default list.</summary>
    private async Task AssertStarOnlyTouchesOwnDefaultAsync()
    {
        var alice = await TestUsers.SignedInClientAsync(factory);
        var bob = await TestUsers.SignedInClientAsync(factory);
        var before = (await alice.GetFromJsonAsync<JsonElement>("/api/v1/watchlists")).GetProperty("items")[0].GetProperty("version").GetInt32();

        await bob.PutAsync("/api/v1/watchlists/default/items/NVDA", null);

        (await alice.GetFromJsonAsync<JsonElement>("/api/v1/watchlists")).GetProperty("items")[0].GetProperty("version").GetInt32().ShouldBe(before);
    }

    /// <summary>Bob addresses Alice's analysis job or result by id: 404, and Alice's job is untouched (not cancelled).</summary>
    private async Task AssertForeignAnalysisIs404Async(HttpMethod method, Func<(Guid JobId, Guid ResultId), string> path)
    {
        var alice = await TestUsers.SignedInClientAsync(factory);
        var bob = await TestUsers.SignedInClientAsync(factory);
        var aliceId = (await alice.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("id").GetGuid();
        var ids = (JobId: Guid.NewGuid(), ResultId: Guid.NewGuid());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();
            var now = DateTimeOffset.UtcNow;
            db.Jobs.Add(new AnalysisJob { Id = ids.JobId, UserId = aliceId, InstrumentId = Guid.NewGuid(), Symbol = "NVDA", IdempotencyKey = "isolation", RequestHash = "isolation", CreatedAt = now, RunAfter = now.AddDays(1) });
            db.Results.Add(new AnalysisResult
            {
                Id = ids.ResultId,
                JobId = ids.JobId,
                UserId = aliceId,
                InstrumentId = Guid.NewGuid(),
                Symbol = "NVDA",
                Provider = "fake",
                Model = "m",
                PromptId = "stock_analysis",
                PromptVersion = "v",
                OutputSchemaVersion = "analysis.v1",
                Context = "{}",
                RawOutput = "{}",
                Parsed = "{}",
                ValidationStatus = "valid",
                ValidationIssues = "[]",
                DataAsOf = now,
                AnalysisAt = now,
            });
            await db.SaveChangesAsync();
        }

        (await bob.SendAsync(new HttpRequestMessage(method, path(ids)))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await alice.GetFromJsonAsync<JsonElement>($"/api/v1/analyses/jobs/{ids.JobId}")).GetProperty("status").GetString().ShouldBe("pending");
    }

    /// <summary>Bob addresses Alice's paper order: 404.</summary>
    /// <summary>Bob addresses a position that isn't his (here one that doesn't exist for him): 404.</summary>
    private async Task AssertUnknownPositionIs404Async(string suffix)
    {
        var bob = await TestUsers.SignedInClientAsync(factory);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/positions/{Guid.NewGuid()}{suffix}") { Content = JsonContent.Create(new { }) };
        request.Headers.Add("Idempotency-Key", $"isolation-{Guid.NewGuid():N}");
        (await bob.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task AssertForeignBrokerConnectionIs404Async()
    {
        var alice = await TestUsers.SignedInClientAsync(factory);
        var bob = await TestUsers.SignedInClientAsync(factory);
        var aliceId = (await alice.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("id").GetGuid();
        var connectionId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PaperDbContext>();
            db.BrokerConnections.Add(new BrokerConnection
            {
                Id = connectionId,
                UserId = aliceId,
                KeySuffix = "3F2A",
                CredentialsCiphertext = [1, 2, 3],
                CredentialsKeyId = "k1",
                ConnectedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        (await bob.DeleteAsync($"/api/v1/broker/connections/{connectionId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await bob.GetFromJsonAsync<JsonElement>("/api/v1/broker/connections")).GetProperty("items").GetArrayLength().ShouldBe(0);

        await using var check = factory.Services.CreateAsyncScope();
        (await check.ServiceProvider.GetRequiredService<PaperDbContext>().BrokerConnections.IgnoreQueryFilters().SingleAsync(c => c.Id == connectionId)).Status.ShouldBe("connected");
    }

    private async Task AssertForeignDeviceIsUntouchedAsync()
    {
        var alice = await TestUsers.SignedInClientAsync(factory);
        var bob = await TestUsers.SignedInClientAsync(factory);
        var deviceId = $"isolation-{Guid.NewGuid():N}";
        (await alice.PostAsJsonAsync("/api/v1/devices", new { deviceId, pushToken = $"ExponentPushToken[{Guid.NewGuid():N}]", platform = "ios" })).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await bob.DeleteAsync($"/api/v1/devices/{deviceId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var scope = factory.Services.CreateAsyncScope();
        var device = await scope.ServiceProvider.GetRequiredService<Astryx.Modules.Notifications.Infrastructure.Persistence.NotificationsDbContext>()
            .PushDevices.IgnoreQueryFilters().SingleAsync(d => d.DeviceId == deviceId);
        device.RevokedAt.ShouldBeNull();
    }

    private async Task AssertUnknownNotificationIs404Async()
    {
        var bob = await TestUsers.SignedInClientAsync(factory);
        (await bob.PostAsync($"/api/v1/notifications/{Guid.NewGuid()}/read", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task AssertUnknownPositionReadIs404Async(string path)
    {
        var bob = await TestUsers.SignedInClientAsync(factory);
        (await bob.GetAsync(path)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task AssertForeignOrderIs404Async(bool cancel = false)
    {
        var alice = await TestUsers.SignedInClientAsync(factory);
        var bob = await TestUsers.SignedInClientAsync(factory);
        var aliceId = (await alice.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("id").GetGuid();
        var id = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PaperDbContext>();
            db.Orders.Add(new Order
            {
                Id = id,
                UserId = aliceId,
                PortfolioId = Guid.NewGuid(),
                InstrumentId = Guid.NewGuid(),
                Symbol = "NVDA",
                ProposalId = Guid.NewGuid(),
                AssessmentId = Guid.NewGuid(),
                ClientOrderId = $"isolation-{id:N}",
                BrokerOrderId = $"SIM-{id:N}"[..10],
                Purpose = "entry",
                Side = "buy",
                OrderType = "buy_stop",
                TimeInForce = "day",
                Quantity = 1,
                StopPrice = 186.5m,
                Status = "working",
                SubmittedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        if (cancel)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/paper/orders/{id}/cancel") { Content = JsonContent.Create(new { }) };
            request.Headers.Add("Idempotency-Key", $"isolation-{Guid.NewGuid():N}");
            (await bob.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            return;
        }

        (await bob.GetAsync($"/api/v1/paper/orders/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await alice.GetAsync($"/api/v1/paper/orders/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>Bob addresses Alice's trade proposal: 404, and it stays pending.</summary>
    private async Task AssertForeignProposalIs404Async(HttpMethod method, string suffix, object? body = null)
    {
        var alice = await TestUsers.SignedInClientAsync(factory);
        var bob = await TestUsers.SignedInClientAsync(factory);
        var aliceId = (await alice.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("id").GetGuid();
        var id = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TradingDbContext>();
            db.Proposals.Add(new Proposal
            {
                Id = id,
                UserId = aliceId,
                PortfolioId = Guid.NewGuid(),
                InstrumentId = Guid.NewGuid(),
                Symbol = "NVDA",
                Source = "signal",
                SignalId = Guid.NewGuid(),
                OrderType = "buy_stop",
                EntryPrice = 186.5m,
                StopPrice = 178.2m,
                TargetPrice = 204m,
                Headline = "h",
                Summary = "s",
                CopySource = "template",
                Evidence = "[]",
                Invalidation = "[]",
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            });
            await db.SaveChangesAsync();
        }

        var request = new HttpRequestMessage(method, $"/api/v1/proposals/{id}{suffix}");
        if (method == HttpMethod.Post)
        {
            request.Content = JsonContent.Create(body ?? new { });
        }

        (await bob.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await alice.GetFromJsonAsync<JsonElement>($"/api/v1/proposals/{id}")).GetProperty("status").GetString().ShouldBe("pending");
    }

    [GeneratedRegex(@"userId=(?<user>[0-9a-f-]{36})&token=(?<token>[A-Za-z0-9_-]{43})")]
    private static partial Regex LinkPattern();

    private List<(string Key, RouteEndpoint Endpoint)> ApiEndpoints() =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true)
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"]).Select(m => ($"{m} {e.RoutePattern.RawText!.TrimEnd('/')}", e)))
            .ToList();

    private static bool RequiresAuthorization(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null;

    [Fact]
    public void Every_api_route_requires_authentication_unless_explicitly_anonymous()
    {
        var endpoints = ApiEndpoints();

        endpoints.Where(e => !AnonymousRoutes.Contains(e.Key) && !RequiresAuthorization(e.Endpoint)).Select(e => e.Key).ShouldBeEmpty();
        endpoints.Where(e => AnonymousRoutes.Contains(e.Key) && RequiresAuthorization(e.Endpoint)).Select(e => e.Key).ShouldBeEmpty();
        AnonymousRoutes.Except(endpoints.Select(e => e.Key)).ShouldBeEmpty(); // no stale allowlist entries
    }

    [Fact]
    public void Every_parameterised_route_has_an_isolation_fixture()
    {
        var parameterised = ApiEndpoints()
            .Where(e => RequiresAuthorization(e.Endpoint) && e.Endpoint.RoutePattern.Parameters.Count > 0)
            .Select(e => e.Key)
            .ToHashSet(StringComparer.Ordinal);

        SharedDataRoutes.Except(parameterised).ShouldBeEmpty(); // no stale exemptions
        parameterised.ExceptWith(SharedDataRoutes);
        parameterised.Except(IsolationFixtures.Keys).ShouldBeEmpty();
        IsolationFixtures.Keys.Except(parameterised).ShouldBeEmpty();
    }

    [Fact]
    public async Task Isolation_fixtures_pass()
    {
        foreach (var (_, fixture) in IsolationFixtures)
        {
            await fixture(this);
        }
    }

    private async Task<Guid> RegisterProvisionedUserAsync()
    {
        var email = $"iso-{Guid.NewGuid():N}@example.com";
        (await _client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = "correct horse battery staple" })).EnsureSuccessStatusCode();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        Guid userId = default;
        while (DateTime.UtcNow < deadline)
        {
            var message = factory.Emails.To(email).FirstOrDefault();
            if (message is not null)
            {
                userId = Guid.Parse(LinkPattern().Match(message.TextBody).Groups["user"].Value);
                await using var scope = factory.Services.CreateAsyncScope();
                var portfolioReady = await scope.ServiceProvider.GetRequiredService<PortfolioDbContext>().Transactions.AnyAsync(t => scope.ServiceProvider.GetRequiredService<PortfolioDbContext>().Portfolios.Any(p => p.Id == t.PortfolioId && p.UserId == userId));
                var watchlistReady = await scope.ServiceProvider.GetRequiredService<WatchlistsDbContext>().Watchlists.AnyAsync(w => w.UserId == userId);
                var profileReady = await scope.ServiceProvider.GetRequiredService<ProfilesDbContext>().NotificationPreferences.AnyAsync(n => n.UserId == userId);
                if (portfolioReady && watchlistReady && profileReady)
                {
                    return userId;
                }
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("user was not provisioned");
    }

    private async Task<(int Watchlists, int Portfolios, int Ledger, int Profiles, int Risk, int Prefs, int VerificationTokens)> VisibleRowsOfAsync(Guid owner, Guid? scope)
    {
        using var _ = UserScope.Begin(scope); // contexts capture the scope when created
        await using var services = factory.Services.CreateAsyncScope();
        var sp = services.ServiceProvider;
        var portfolio = sp.GetRequiredService<PortfolioDbContext>();
        var profiles = sp.GetRequiredService<ProfilesDbContext>();
        var ownerPortfolioIds = await portfolio.Portfolios.IgnoreQueryFilters().Where(p => p.UserId == owner).Select(p => p.Id).ToListAsync();
        return (
            await sp.GetRequiredService<WatchlistsDbContext>().Watchlists.CountAsync(w => w.UserId == owner),
            await portfolio.Portfolios.CountAsync(p => p.UserId == owner),
            await portfolio.Transactions.CountAsync(t => ownerPortfolioIds.Contains(t.PortfolioId)),
            await profiles.Profiles.CountAsync(p => p.UserId == owner),
            await profiles.RiskSettings.CountAsync(r => r.UserId == owner),
            await profiles.NotificationPreferences.CountAsync(n => n.UserId == owner),
            await sp.GetRequiredService<IdentityDbContext>().UserTokens.CountAsync(t => t.UserId == owner));
    }

    [Fact]
    public async Task Queries_in_another_users_scope_cannot_see_the_owners_rows()
    {
        var alice = await RegisterProvisionedUserAsync();
        var bob = await RegisterProvisionedUserAsync();

        var asBob = await VisibleRowsOfAsync(alice, bob);
        var asAlice = await VisibleRowsOfAsync(alice, alice);
        var unscoped = await VisibleRowsOfAsync(alice, null);

        asBob.ShouldBe((0, 0, 0, 0, 0, 0, 0));
        asAlice.ShouldBe((1, 1, 1, 1, 1, 1, 1));
        unscoped.ShouldBe(asAlice); // workers and anonymous flows address rows explicitly
    }

    [Fact]
    public async Task Writes_in_another_users_scope_cannot_find_the_owners_rows_to_change()
    {
        var alice = await RegisterProvisionedUserAsync();
        var bob = await RegisterProvisionedUserAsync();

        using var _ = UserScope.Begin(bob);
        await using var services = factory.Services.CreateAsyncScope();
        var db = services.ServiceProvider.GetRequiredService<WatchlistsDbContext>();
        var renamed = await db.Watchlists.Where(w => w.UserId == alice).ExecuteUpdateAsync(s => s.SetProperty(w => w.Name, "hijacked"));

        renamed.ShouldBe(0);
    }
}
