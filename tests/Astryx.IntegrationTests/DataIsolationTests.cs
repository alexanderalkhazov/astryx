using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Astryx.Infrastructure.Security;
using Astryx.Modules.Identity.Infrastructure.Persistence;
using Astryx.Modules.Portfolio.Infrastructure.Persistence;
using Astryx.Modules.Profiles.Infrastructure.Persistence;
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
        // e.g. ["GET /api/v1/watchlists/{id}"] = t => t.AssertForeignWatchlistIs404Async(),
    };

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
