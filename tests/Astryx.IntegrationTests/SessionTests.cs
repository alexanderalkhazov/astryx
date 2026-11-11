using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Astryx.Modules.Audit.Infrastructure.Persistence;
using Astryx.Modules.Identity.Application;
using Astryx.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Astryx.IntegrationTests;

/// <summary>Login, refresh rotation, reuse detection, lockout, logout (T-021, SYSTEM_DESIGN.md §18.3, §21.6).</summary>
public partial class SessionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private const string Password = "correct horse battery staple";

    private sealed record Tokens(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt, string TokenType);

    [GeneratedRegex(@"userId=(?<user>[0-9a-f-]{36})&token=(?<token>[A-Za-z0-9_-]{43})")]
    private static partial Regex LinkPattern();

    private async Task<(string Email, Guid UserId)> CreateUserAsync(bool verify = true)
    {
        var email = $"session-{Guid.NewGuid():N}@example.com";
        (await _client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = Password })).EnsureSuccessStatusCode();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!factory.Emails.To(email).Any() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        var match = LinkPattern().Match(factory.Emails.To(email).Single().TextBody);
        var userId = Guid.Parse(match.Groups["user"].Value);
        if (verify)
        {
            (await _client.PostAsJsonAsync("/api/v1/auth/verify-email", new { userId, token = match.Groups["token"].Value })).EnsureSuccessStatusCode();
        }

        return (email, userId);
    }

    private Task<HttpResponseMessage> LoginRawAsync(string email, string password = Password, string deviceId = "ios-device-0001") =>
        _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password, deviceId });

    private async Task<Tokens> LoginAsync(string email, string deviceId = "ios-device-0001")
    {
        var response = await LoginRawAsync(email, deviceId: deviceId);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!;
    }

    private Task<HttpResponseMessage> RefreshRawAsync(string refreshToken, string deviceId = "ios-device-0001") =>
        _client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken, deviceId });

    private async Task<HttpResponseMessage> GetMeAsync(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> PostAuthorizedAsync(string path, string accessToken, object? body = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _client.SendAsync(request);
    }

    private static async Task<(string Code, string Detail)> ProblemAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (problem.GetProperty("code").GetString()!, problem.GetProperty("detail").GetString()!);
    }

    private async Task ExecuteSqlAsync(FormattableString sql)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.ExecuteSqlAsync(sql);
    }

    [Fact]
    public async Task Full_session_lifecycle_login_use_refresh_logout()
    {
        var (email, userId) = await CreateUserAsync();

        var tokens = await LoginAsync(email);
        tokens.TokenType.ShouldBe("Bearer");
        (tokens.AccessTokenExpiresAt - DateTimeOffset.UtcNow).ShouldBeInRange(TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15.1));
        (tokens.RefreshTokenExpiresAt - DateTimeOffset.UtcNow).ShouldBeInRange(TimeSpan.FromDays(29.9), TimeSpan.FromDays(30.1));

        var me = await GetMeAsync(tokens.AccessToken);
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await me.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid().ShouldBe(userId);

        var refreshed = (await (await RefreshRawAsync(tokens.RefreshToken)).Content.ReadFromJsonAsync<Tokens>())!;
        refreshed.RefreshToken.ShouldNotBe(tokens.RefreshToken);
        (await GetMeAsync(refreshed.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await PostAuthorizedAsync("/api/v1/auth/logout", refreshed.AccessToken, new { refreshToken = refreshed.RefreshToken })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var afterLogout = await RefreshRawAsync(refreshed.RefreshToken);
        afterLogout.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ProblemAsync(afterLogout)).Code.ShouldBe("session_revoked");
    }

    [Fact]
    public async Task Access_token_is_ES256_with_kid_issuer_audience_and_15_minute_lifetime()
    {
        var (email, userId) = await CreateUserAsync();
        var tokens = await LoginAsync(email);

        var jwt = new JsonWebToken(tokens.AccessToken);

        jwt.Alg.ShouldBe("ES256");
        jwt.Kid.ShouldBe("test-key");
        jwt.Issuer.ShouldBe("astryx-api");
        jwt.Audiences.ShouldBe(["astryx-mobile"]);
        jwt.Subject.ShouldBe(userId.ToString());
        jwt.GetClaim("sid").Value.ShouldNotBeNullOrWhiteSpace();
        jwt.GetClaim("sstamp").Value.Length.ShouldBe(16);
        (jwt.ValidTo - jwt.IssuedAt).ShouldBe(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task Rotated_token_reused_after_the_grace_window_revokes_the_whole_family_and_is_audited()
    {
        var (email, userId) = await CreateUserAsync();
        var a = await LoginAsync(email);
        var b = (await (await RefreshRawAsync(a.RefreshToken)).Content.ReadFromJsonAsync<Tokens>())!;
        var c = (await (await RefreshRawAsync(b.RefreshToken)).Content.ReadFromJsonAsync<Tokens>())!;
        await ExecuteSqlAsync($"UPDATE identity.refresh_tokens SET revoked_at = now() - interval '1 minute' WHERE user_id = {userId} AND revoked_reason = 'rotated'");

        var replay = await RefreshRawAsync(b.RefreshToken); // an attacker replays a stolen, already-rotated token

        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ProblemAsync(replay)).Code.ShouldBe("session_revoked");
        (await ProblemAsync(await RefreshRawAsync(c.RefreshToken))).Code.ShouldBe("session_revoked"); // the legitimate holder is signed out too
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AuditDbContext>().AuditLogs.AnyAsync(l => l.UserId == userId && l.Action == "auth.refresh_reuse_detected")).ShouldBeTrue();
    }

    [Fact]
    public async Task Rotated_token_reused_within_the_grace_window_is_a_race_not_theft()
    {
        var (email, _) = await CreateUserAsync();
        var a = await LoginAsync(email);
        var b = (await (await RefreshRawAsync(a.RefreshToken)).Content.ReadFromJsonAsync<Tokens>())!;

        var duplicate = await RefreshRawAsync(a.RefreshToken);

        duplicate.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ProblemAsync(duplicate)).Code.ShouldBe("refresh_token_superseded");
        (await RefreshRawAsync(b.RefreshToken)).StatusCode.ShouldBe(HttpStatusCode.OK); // family still alive
    }

    [Fact]
    public async Task Concurrent_refreshes_of_one_token_rotate_exactly_once()
    {
        var (email, _) = await CreateUserAsync();
        var tokens = await LoginAsync(email);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => RefreshRawAsync(tokens.RefreshToken)));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        foreach (var loser in responses.Where(r => r.StatusCode != HttpStatusCode.OK))
        {
            (await ProblemAsync(loser)).Code.ShouldBe("refresh_token_superseded");
        }

        var winner = (await responses.Single(r => r.StatusCode == HttpStatusCode.OK).Content.ReadFromJsonAsync<Tokens>())!;
        (await RefreshRawAsync(winner.RefreshToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_token_from_another_device_is_treated_as_stolen()
    {
        var (email, _) = await CreateUserAsync();
        var tokens = await LoginAsync(email, deviceId: "ios-device-0001");

        var stolen = await RefreshRawAsync(tokens.RefreshToken, deviceId: "attacker-device-99");

        (await ProblemAsync(stolen)).Code.ShouldBe("session_revoked");
        (await ProblemAsync(await RefreshRawAsync(tokens.RefreshToken))).Code.ShouldBe("session_revoked");
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_with_responses_identical_to_an_unknown_email()
    {
        var (email, _) = await CreateUserAsync();
        var unknown = await ProblemAsync(await LoginRawAsync($"nobody-{Guid.NewGuid():N}@example.com"));

        for (var attempt = 1; attempt <= SessionService.MaxFailedAttempts; attempt++)
        {
            var failed = await LoginRawAsync(email, password: "wrong password here");
            failed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await ProblemAsync(failed)).ShouldBe(unknown);
        }

        var lockedOut = await LoginRawAsync(email); // correct password, but locked
        lockedOut.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ProblemAsync(lockedOut)).ShouldBe(unknown);

        await ExecuteSqlAsync($"UPDATE identity.users SET lockout_end = now() - interval '1 second' WHERE email = {email}");
        (await LoginRawAsync(email)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unverified_email_is_reported_only_after_a_correct_password()
    {
        var (email, _) = await CreateUserAsync(verify: false);

        var wrong = await LoginRawAsync(email, password: "wrong password here");
        var right = await LoginRawAsync(email);

        (await ProblemAsync(wrong)).Code.ShouldBe("invalid_credentials");
        right.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ProblemAsync(right)).Code.ShouldBe("email_not_verified");
    }

    [Fact]
    public async Task Expired_access_token_returns_token_expired()
    {
        var (email, userId) = await CreateUserAsync();
        var tokens = await LoginAsync(email);
        var stamp = new JsonWebToken(tokens.AccessToken).GetClaim("sstamp").Value;
        var pastIssuer = new AccessTokenIssuer(
            factory.Services.GetRequiredService<JwtKeyRing>(),
            factory.Services.GetRequiredService<IOptions<JwtOptions>>(),
            new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(-1)));
        var (expired, _) = pastIssuer.Issue(userId, Guid.NewGuid(), "irrelevant");

        var response = await GetMeAsync(expired);

        stamp.ShouldNotBeNull();
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ProblemAsync(response)).Code.ShouldBe("token_expired");
    }

    [Fact]
    public async Task Tokens_with_wrong_audience_bad_signature_or_none_are_rejected()
    {
        var (email, userId) = await CreateUserAsync();
        var tokens = await LoginAsync(email);
        var keys = factory.Services.GetRequiredService<JwtKeyRing>();
        var wrongAudience = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "astryx-api",
            Audience = "some-other-app",
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = keys.SigningCredentials,
            Subject = new ClaimsIdentity([new Claim("sub", userId.ToString())]),
        });
        var foreignKey = new JwtKeyRing(Options.Create(new JwtOptions { SigningKeys = JwtKeyRing.Generate("test-key") }));
        var forged = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "astryx-api",
            Audience = "astryx-mobile",
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = foreignKey.SigningCredentials,
            Subject = new ClaimsIdentity([new Claim("sub", userId.ToString())]),
        });

        foreach (var token in new[] { wrongAudience, forged, tokens.AccessToken[..^4] + "AAAA" })
        {
            var response = await GetMeAsync(token);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await ProblemAsync(response)).Code.ShouldBe("unauthorized");
        }

        var anonymous = await _client.GetAsync("/api/v1/me");
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        anonymous.Headers.WwwAuthenticate.ToString().ShouldContain("Bearer");
    }

    [Fact]
    public async Task Logout_all_revokes_every_device_and_invalidates_live_access_tokens()
    {
        var (email, userId) = await CreateUserAsync();
        var phone = await LoginAsync(email, deviceId: "ios-device-0001");
        var tablet = await LoginAsync(email, deviceId: "ipad-device-0002");

        (await PostAuthorizedAsync("/api/v1/auth/logout-all", phone.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ProblemAsync(await RefreshRawAsync(phone.RefreshToken, "ios-device-0001"))).Code.ShouldBe("session_revoked");
        (await ProblemAsync(await RefreshRawAsync(tablet.RefreshToken, "ipad-device-0002"))).Code.ShouldBe("session_revoked");
        var stale = await GetMeAsync(tablet.AccessToken); // signed before the security stamp rotated
        stale.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ProblemAsync(stale)).Code.ShouldBe("session_revoked");
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AuditDbContext>().AuditLogs.AnyAsync(l => l.UserId == userId && l.Action == "auth.logout_all")).ShouldBeTrue();
    }

    [Fact]
    public async Task Signing_in_again_on_the_same_device_replaces_the_previous_session()
    {
        var (email, _) = await CreateUserAsync();
        var first = await LoginAsync(email);
        var second = await LoginAsync(email);

        (await ProblemAsync(await RefreshRawAsync(first.RefreshToken))).Code.ShouldBe("session_revoked");
        (await RefreshRawAsync(second.RefreshToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Expired_refresh_token_and_families_past_90_days_are_rejected()
    {
        var (email, userId) = await CreateUserAsync();
        var a = await LoginAsync(email, deviceId: "ios-device-0001");
        var b = await LoginAsync(email, deviceId: "ipad-device-0002");
        await ExecuteSqlAsync($"UPDATE identity.refresh_tokens SET expires_at = now() - interval '1 second' WHERE user_id = {userId} AND device_id = 'ios-device-0001'");
        await ExecuteSqlAsync($"UPDATE identity.refresh_tokens SET family_started_at = now() - interval '91 days' WHERE user_id = {userId} AND device_id = 'ipad-device-0002'");

        (await ProblemAsync(await RefreshRawAsync(a.RefreshToken, "ios-device-0001"))).Code.ShouldBe("session_revoked");
        (await ProblemAsync(await RefreshRawAsync(b.RefreshToken, "ipad-device-0002"))).Code.ShouldBe("session_revoked");
    }

    [Fact]
    public async Task Refresh_tokens_are_stored_only_as_hashes()
    {
        var (email, userId) = await CreateUserAsync();
        var tokens = await LoginAsync(email);

        await using var scope = factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().RefreshTokens.AsNoTracking().SingleAsync(t => t.UserId == userId);
        stored.TokenHash.Length.ShouldBe(32);
        Convert.ToBase64String(stored.TokenHash).ShouldNotContain(tokens.RefreshToken);
    }
}
