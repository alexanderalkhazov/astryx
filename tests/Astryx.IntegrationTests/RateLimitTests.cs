using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Astryx.IntegrationTests;

/// <summary>An API with tight limits so tests can reach them quickly.</summary>
public sealed class LowLimitApiFactory : ApiFactory
{
    protected override IReadOnlyDictionary<string, string> ExtraSettings => new Dictionary<string, string>
    {
        ["RateLimits:AuthPerMinute"] = "3",
        ["RateLimits:AuthPerHour"] = "100",
        ["RateLimits:UserPerMinute"] = "5",
    };
}

/// <summary>Rate limiting (T-025, SYSTEM_DESIGN.md §18.10).</summary>
public class RateLimitTests(LowLimitApiFactory factory) : IClassFixture<LowLimitApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private Task<HttpResponseMessage> LoginAsync(string email) =>
        _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "wrong but well-formed password", deviceId = "ios-device-0001" });

    [Fact]
    public async Task Auth_endpoints_return_429_with_retry_after_once_the_per_ip_limit_is_reached()
    {
        // Login, register, forgot and reset share the per-IP auth budget (3/min in this factory).
        var responses = new List<HttpResponseMessage>();
        for (var i = 0; i < 4; i++)
        {
            responses.Add(await LoginAsync($"nobody-{i}@example.com"));
        }

        responses.Take(3).ShouldAllBe(r => r.StatusCode == HttpStatusCode.Unauthorized);
        var limited = responses[3];
        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        int.Parse(limited.Headers.GetValues("Retry-After").Single()).ShouldBeInRange(1, 60);
        var problem = await limited.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().ShouldBe("rate_limited");
        problem.GetProperty("status").GetInt32().ShouldBe(429);

        var forgot = await _client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = "nobody@example.com" });
        forgot.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Health_checks_are_never_limited()
    {
        for (var i = 0; i < 20; i++)
        {
            (await _client.GetAsync("/health/live")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }
}

/// <summary>Per-user limit on authenticated requests, in its own factory so the auth budget above stays separate.</summary>
public sealed class UserLimitApiFactory : ApiFactory
{
    protected override IReadOnlyDictionary<string, string> ExtraSettings => new Dictionary<string, string>
    {
        ["RateLimits:UserPerMinute"] = "5",
    };
}

public partial class UserRateLimitTests(UserLimitApiFactory factory) : IClassFixture<UserLimitApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [GeneratedRegex(@"userId=(?<user>[0-9a-f-]{36})&token=(?<token>[A-Za-z0-9_-]{43})")]
    private static partial Regex LinkPattern();

    private async Task<string> AccessTokenAsync()
    {
        var email = $"limit-{Guid.NewGuid():N}@example.com";
        (await _client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = "correct horse battery staple" })).EnsureSuccessStatusCode();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!factory.Emails.To(email).Any() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        var match = LinkPattern().Match(factory.Emails.To(email).Single().TextBody);
        await TestUsers.VerifyEmailAsync(_client, Guid.Parse(match.Groups["user"].Value), match.Groups["token"].Value);
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "correct horse battery staple", deviceId = "ios-device-0001" });
        return (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }

    private async Task<HttpStatusCode> GetMeAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await _client.SendAsync(request)).StatusCode;
    }

    [Fact]
    public async Task Each_signed_in_user_has_their_own_request_budget()
    {
        var alice = await AccessTokenAsync();
        var bob = await AccessTokenAsync();

        var aliceResults = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            aliceResults.Add(await GetMeAsync(alice));
        }

        aliceResults.Take(5).ShouldAllBe(s => s == HttpStatusCode.OK);
        aliceResults[5].ShouldBe(HttpStatusCode.TooManyRequests);
        (await GetMeAsync(bob)).ShouldBe(HttpStatusCode.OK); // Alice exhausting her budget does not affect Bob
    }
}
