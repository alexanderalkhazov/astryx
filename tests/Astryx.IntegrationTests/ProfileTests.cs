using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Audit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.IntegrationTests;

/// <summary>Current user, profile, risk limits, notifications, trading mode (T-023, SYSTEM_DESIGN.md §11.3).</summary>
public partial class ProfileTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private const string Password = "correct horse battery staple";

    private const string ValidRisk = """
        {"maxRiskPerTradePct":"0.005","maxPositionWeightPct":"0.10","maxSectorExposurePct":"0.30","sectorWarningPct":"0.25",
         "dailyLossHaltPct":"0.015","maxOpenPositions":10,"minRewardRisk":"2.0"}
        """;

    [GeneratedRegex(@"userId=(?<user>[0-9a-f-]{36})&token=(?<token>[A-Za-z0-9_-]{43})")]
    private static partial Regex LinkPattern();

    private async Task<(Guid UserId, string AccessToken)> SignedInUserAsync(string? displayName = "Alex")
    {
        var email = $"profile-{Guid.NewGuid():N}@example.com";
        (await _client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = Password, displayName })).EnsureSuccessStatusCode();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!factory.Emails.To(email).Any() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        var match = LinkPattern().Match(factory.Emails.To(email).Single().TextBody);
        var userId = Guid.Parse(match.Groups["user"].Value);
        (await _client.PostAsJsonAsync("/api/v1/auth/verify-email", new { userId, token = match.Groups["token"].Value })).EnsureSuccessStatusCode();
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password, deviceId = "ios-device-0001" });
        var accessToken = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        return (userId, accessToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string accessToken, string? json = null, string? ifMatch = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Me_returns_account_profile_and_feature_flags()
    {
        var (userId, token) = await SignedInUserAsync("Alex");

        var me = await JsonAsync(await SendAsync(HttpMethod.Get, "/api/v1/me", token));

        me.GetProperty("id").GetGuid().ShouldBe(userId);
        me.GetProperty("emailVerified").GetBoolean().ShouldBeTrue();
        me.GetProperty("displayName").GetString().ShouldBe("Alex");
        me.GetProperty("timezone").GetString().ShouldBe("America/New_York");
        me.GetProperty("tradingMode").GetString().ShouldBe("paper");
        me.GetProperty("features").GetProperty("liveTrading").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Patch_me_updates_profile_fields()
    {
        var (_, token) = await SignedInUserAsync();

        var response = await SendAsync(HttpMethod.Patch, "/api/v1/me", token, """{"displayName":"Trader A","timezone":"Europe/London","locale":"en-GB","biometricLock":true}""");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var me = await JsonAsync(response);
        me.GetProperty("displayName").GetString().ShouldBe("Trader A");
        me.GetProperty("timezone").GetString().ShouldBe("Europe/London");
        me.GetProperty("locale").GetString().ShouldBe("en-GB");
        me.GetProperty("biometricLock").GetBoolean().ShouldBeTrue();
    }

    [Theory]
    [InlineData("""{"timezone":"Mars/Olympus"}""", "timezone")]
    [InlineData("""{"locale":"english"}""", "locale")]
    public async Task Patch_me_rejects_invalid_values(string body, string field)
    {
        var (_, token) = await SignedInUserAsync();

        var response = await SendAsync(HttpMethod.Patch, "/api/v1/me", token, body);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(response)).GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Risk_settings_start_at_the_design_defaults_with_bounds_and_an_etag()
    {
        var (_, token) = await SignedInUserAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/v1/me/settings/risk", token);

        response.Headers.ETag!.Tag.ShouldBe("\"v1\"");
        var risk = await JsonAsync(response);
        risk.GetProperty("maxRiskPerTradePct").GetString().ShouldBe("0.007500");
        risk.GetProperty("maxPositionWeightPct").GetString().ShouldBe("0.120000");
        risk.GetProperty("maxSectorExposurePct").GetString().ShouldBe("0.350000");
        risk.GetProperty("dailyLossHaltPct").GetString().ShouldBe("0.020000");
        risk.GetProperty("maxOpenPositions").GetInt32().ShouldBe(12);
        risk.GetProperty("bounds").GetProperty("maxPositionWeightPct").GetProperty("max").GetString().ShouldBe("0.25");
        risk.GetProperty("bounds").GetProperty("maxOpenPositions").GetProperty("max").GetInt32().ShouldBe(30);
    }

    [Fact]
    public async Task Updating_risk_settings_bumps_the_version_publishes_an_event_and_is_audited()
    {
        var (userId, token) = await SignedInUserAsync();

        var response = await SendAsync(HttpMethod.Put, "/api/v1/me/settings/risk", token, ValidRisk, ifMatch: "\"v1\"");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag!.Tag.ShouldBe("\"v2\"");
        var risk = await JsonAsync(response);
        risk.GetProperty("maxRiskPerTradePct").GetString().ShouldBe("0.005");
        risk.GetProperty("version").GetInt32().ShouldBe(2);

        await using var scope = factory.Services.CreateAsyncScope();
        var outbox = await scope.ServiceProvider.GetRequiredService<InfraDbContext>().OutboxMessages.AsNoTracking()
            .Where(m => m.Type == "profiles.risk_settings_changed.v1").Select(m => m.Payload).ToListAsync();
        outbox.ShouldContain(p => p.Contains(userId.ToString()) && p.Contains("\"version\": 2"));
        var audit = await scope.ServiceProvider.GetRequiredService<AuditDbContext>().AuditLogs.AsNoTracking()
            .SingleAsync(l => l.UserId == userId && l.Action == "risk_settings.updated");
        audit.Before!.ShouldContain("0.0075");
        audit.After!.ShouldContain("0.005");
    }

    [Fact]
    public async Task Risk_update_without_if_match_or_with_a_stale_etag_is_refused()
    {
        var (_, token) = await SignedInUserAsync();
        (await SendAsync(HttpMethod.Put, "/api/v1/me/settings/risk", token, ValidRisk, ifMatch: "\"v1\"")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var missing = await SendAsync(HttpMethod.Put, "/api/v1/me/settings/risk", token, ValidRisk);
        var stale = await SendAsync(HttpMethod.Put, "/api/v1/me/settings/risk", token, ValidRisk, ifMatch: "\"v1\"");
        var garbage = await SendAsync(HttpMethod.Put, "/api/v1/me/settings/risk", token, ValidRisk, ifMatch: "banana");

        missing.StatusCode.ShouldBe((HttpStatusCode)428);
        (await JsonAsync(missing)).GetProperty("code").GetString().ShouldBe("precondition_required");
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await JsonAsync(stale)).GetProperty("code").GetString().ShouldBe("precondition_failed");
        garbage.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
    }

    [Theory]
    [InlineData("maxRiskPerTradePct", "\"0.05\"")]        // above 2%
    [InlineData("maxPositionWeightPct", "\"0.01\"")]      // below 2%
    [InlineData("dailyLossHaltPct", "\"0.0123456\"")]     // 7 decimal places would be silently rounded
    [InlineData("sectorWarningPct", "\"0.30\"")]          // must be below max sector exposure (0.30 in this body)
    [InlineData("maxOpenPositions", "31")]
    [InlineData("minRewardRisk", "\"0.5\"")]
    public async Task Risk_values_outside_the_bounds_are_rejected(string field, string value)
    {
        var (_, token) = await SignedInUserAsync();
        var body = JsonNodeReplace(ValidRisk, field, value);

        var response = await SendAsync(HttpMethod.Put, "/api/v1/me/settings/risk", token, body, ifMatch: "\"v1\"");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(response)).GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Notification_preferences_round_trip()
    {
        var (_, token) = await SignedInUserAsync();
        var defaults = await JsonAsync(await SendAsync(HttpMethod.Get, "/api/v1/me/settings/notifications", token));
        defaults.GetProperty("fills").GetBoolean().ShouldBeTrue();
        defaults.GetProperty("dailySummary").GetBoolean().ShouldBeFalse();

        var updated = await JsonAsync(await SendAsync(HttpMethod.Put, "/api/v1/me/settings/notifications", token,
            """{"fills":true,"signals":false,"stopProximity":true,"strategyGuard":false,"system":true,"dailySummary":true,"pushEnabled":true}"""));

        updated.GetProperty("signals").GetBoolean().ShouldBeFalse();
        updated.GetProperty("dailySummary").GetBoolean().ShouldBeTrue();
        (await JsonAsync(await SendAsync(HttpMethod.Get, "/api/v1/me/settings/notifications", token))).GetProperty("pushEnabled").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Requesting_live_trading_is_refused_and_audited()
    {
        var (userId, token) = await SignedInUserAsync();

        var live = await SendAsync(HttpMethod.Put, "/api/v1/me/settings/trading-mode", token, """{"mode":"live","confirmation":"LIVE"}""");
        var paper = await SendAsync(HttpMethod.Put, "/api/v1/me/settings/trading-mode", token, """{"mode":"paper"}""");

        live.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await JsonAsync(live)).GetProperty("code").GetString().ShouldBe("live_trading_disabled");
        paper.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonAsync(paper)).GetProperty("mode").GetString().ShouldBe("paper");
        (await JsonAsync(await SendAsync(HttpMethod.Get, "/api/v1/me", token))).GetProperty("tradingMode").GetString().ShouldBe("paper");
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AuditDbContext>().AuditLogs.AnyAsync(l => l.UserId == userId && l.Action == "trading_mode.live_attempt")).ShouldBeTrue();
    }

    [Theory]
    [InlineData("GET", "/api/v1/me")]
    [InlineData("PATCH", "/api/v1/me")]
    [InlineData("GET", "/api/v1/me/settings/risk")]
    [InlineData("PUT", "/api/v1/me/settings/risk")]
    [InlineData("GET", "/api/v1/me/settings/notifications")]
    [InlineData("PUT", "/api/v1/me/settings/trading-mode")]
    public async Task Every_me_endpoint_requires_authentication(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };

        (await _client.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Each_user_sees_only_their_own_settings()
    {
        var (_, tokenA) = await SignedInUserAsync();
        var (_, tokenB) = await SignedInUserAsync();
        (await SendAsync(HttpMethod.Put, "/api/v1/me/settings/risk", tokenA, ValidRisk, ifMatch: "\"v1\"")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var b = await JsonAsync(await SendAsync(HttpMethod.Get, "/api/v1/me/settings/risk", tokenB));

        b.GetProperty("maxRiskPerTradePct").GetString().ShouldBe("0.007500");
        b.GetProperty("version").GetInt32().ShouldBe(1);
    }

    private static string JsonNodeReplace(string json, string field, string rawValue)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        node[field] = System.Text.Json.Nodes.JsonNode.Parse(rawValue);
        return node.ToJsonString();
    }
}
