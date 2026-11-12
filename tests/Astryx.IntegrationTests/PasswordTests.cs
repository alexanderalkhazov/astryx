using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Astryx.Modules.Audit.Infrastructure.Persistence;
using Astryx.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.IntegrationTests;

/// <summary>Forgot / reset / change password (T-022, SYSTEM_DESIGN.md §11.3 A8, A9, A12; §18.4).</summary>
public partial class PasswordTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private const string Password = "correct horse battery staple";
    private const string NewPassword = "an even longer new passphrase";
    private const string ResetSubject = "Reset your Astryx password";
    private const string ChangedSubject = "Your Astryx password was changed";

    private sealed record Tokens(string AccessToken, string RefreshToken);

    [GeneratedRegex(@"(?<kind>verify-email|reset-password)\?userId=(?<user>[0-9a-f-]{36})&token=(?<token>[A-Za-z0-9_-]{43})")]
    private static partial Regex LinkPattern();

    private async Task<(string Subject, Guid UserId, string Token)> WaitForLinkAsync(string email, string subject, int count = 1)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (factory.Emails.To(email).Count(m => m.Subject == subject) < count)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"No '{subject}' email #{count} for {email}");
            }

            await Task.Delay(100);
        }

        var match = LinkPattern().Match(factory.Emails.To(email).Where(m => m.Subject == subject).ElementAt(count - 1).TextBody);
        return (subject, Guid.Parse(match.Groups["user"].Value), match.Groups["token"].Value);
    }

    private async Task WaitForSubjectAsync(string email, string subject)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!factory.Emails.To(email).Any(m => m.Subject == subject))
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"No '{subject}' email for {email}");
            }

            await Task.Delay(100);
        }
    }

    private async Task<(string Email, Guid UserId)> CreateUserAsync(bool verify = true)
    {
        var email = $"pw-{Guid.NewGuid():N}@example.com";
        (await _client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = Password })).EnsureSuccessStatusCode();
        var (_, userId, token) = await WaitForLinkAsync(email, "Verify your Astryx email");
        if (verify)
        {
            await TestUsers.VerifyEmailAsync(_client, userId, token);
        }

        return (email, userId);
    }

    private Task<HttpResponseMessage> LoginRawAsync(string email, string password, string deviceId = "ios-device-0001") =>
        _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password, deviceId });

    private async Task<Tokens> LoginAsync(string email, string password = Password, string deviceId = "ios-device-0001")
    {
        var response = await LoginRawAsync(email, password, deviceId);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!;
    }

    private Task<HttpResponseMessage> RefreshRawAsync(string refreshToken, string deviceId = "ios-device-0001") =>
        _client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken, deviceId });

    private Task<HttpResponseMessage> ForgotAsync(string email) => _client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email });

    private Task<HttpResponseMessage> ResetAsync(Guid userId, string token, string newPassword = NewPassword) =>
        _client.PostAsJsonAsync("/api/v1/auth/reset-password", new { userId, token, newPassword });

    private async Task<HttpResponseMessage> ChangeAsync(string accessToken, string currentPassword, string newPassword)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/me/change-password") { Content = JsonContent.Create(new { currentPassword, newPassword }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> GetMeAsync(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _client.SendAsync(request);
    }

    private static async Task<string> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;

    [Fact]
    public async Task Forgot_password_for_an_unknown_email_looks_the_same_and_sends_nothing()
    {
        var unknown = $"nobody-{Guid.NewGuid():N}@example.com";
        var (known, _) = await CreateUserAsync();

        var unknownResponse = await ForgotAsync(unknown);
        var knownResponse = await ForgotAsync(known);

        unknownResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        knownResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await unknownResponse.Content.ReadAsStringAsync()).ShouldBe(await knownResponse.Content.ReadAsStringAsync());
        await WaitForLinkAsync(known, ResetSubject);
        factory.Emails.To(unknown).ShouldBeEmpty();
    }

    [Fact]
    public async Task Reset_sets_the_new_password_ends_all_sessions_and_notifies_the_owner()
    {
        var (email, userId) = await CreateUserAsync();
        var phone = await LoginAsync(email, deviceId: "ios-device-0001");
        var tablet = await LoginAsync(email, deviceId: "ipad-device-0002");

        await ForgotAsync(email);
        var (_, linkUser, token) = await WaitForLinkAsync(email, ResetSubject);
        linkUser.ShouldBe(userId);
        (await ResetAsync(userId, token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await CodeAsync(await LoginRawAsync(email, Password))).ShouldBe("invalid_credentials");
        (await LoginRawAsync(email, NewPassword)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CodeAsync(await RefreshRawAsync(phone.RefreshToken, "ios-device-0001"))).ShouldBe("session_revoked");
        (await CodeAsync(await RefreshRawAsync(tablet.RefreshToken, "ipad-device-0002"))).ShouldBe("session_revoked");
        (await CodeAsync(await GetMeAsync(tablet.AccessToken))).ShouldBe("session_revoked");
        await WaitForSubjectAsync(email, ChangedSubject);

        var reused = await ResetAsync(userId, token, "yet another long passphrase");
        reused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(reused)).ShouldBe("invalid_or_expired_token");
    }

    [Fact]
    public async Task Expired_reset_link_is_rejected()
    {
        var (email, userId) = await CreateUserAsync();
        await ForgotAsync(email);
        var (_, _, token) = await WaitForLinkAsync(email, ResetSubject);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database
                .ExecuteSqlAsync($"UPDATE identity.user_tokens SET expires_at = now() - interval '1 second' WHERE user_id = {userId} AND purpose = 'password_reset'");
        }

        (await CodeAsync(await ResetAsync(userId, token))).ShouldBe("invalid_or_expired_token");
    }

    [Fact]
    public async Task Reset_link_expires_after_30_minutes()
    {
        var (email, userId) = await CreateUserAsync();
        await ForgotAsync(email);
        await WaitForLinkAsync(email, ResetSubject);

        await using var scope = factory.Services.CreateAsyncScope();
        var token = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().UserTokens.AsNoTracking()
            .SingleAsync(t => t.UserId == userId && t.Purpose == "password_reset");
        (token.ExpiresAt - token.CreatedAt).ShouldBe(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task A_newer_reset_request_invalidates_the_older_link()
    {
        var (email, userId) = await CreateUserAsync();
        await ForgotAsync(email);
        var (_, _, first) = await WaitForLinkAsync(email, ResetSubject);
        await ForgotAsync(email);
        var (_, _, second) = await WaitForLinkAsync(email, ResetSubject, count: 2);

        (await CodeAsync(await ResetAsync(userId, first))).ShouldBe("invalid_or_expired_token");
        (await ResetAsync(userId, second)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Reset_and_verification_tokens_are_not_interchangeable()
    {
        var email = $"pw-{Guid.NewGuid():N}@example.com";
        (await _client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = Password })).EnsureSuccessStatusCode();
        var (_, userId, verificationToken) = await WaitForLinkAsync(email, "Verify your Astryx email");
        await ForgotAsync(email);
        var (_, _, resetToken) = await WaitForLinkAsync(email, ResetSubject);

        (await CodeAsync(await ResetAsync(userId, verificationToken))).ShouldBe("invalid_or_expired_token");
        (await CodeAsync(await _client.PostAsJsonAsync("/api/v1/auth/verify-email", new { userId, token = resetToken }))).ShouldBe("invalid_or_expired_token");
    }

    [Fact]
    public async Task Reset_clears_a_lockout_and_confirms_an_unverified_email()
    {
        var (email, userId) = await CreateUserAsync(verify: false);
        for (var i = 0; i < 5; i++)
        {
            await LoginRawAsync(email, "wrong password here");
        }

        await ForgotAsync(email);
        var (_, _, token) = await WaitForLinkAsync(email, ResetSubject);
        (await ResetAsync(userId, token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await LoginRawAsync(email, NewPassword)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reset_rejects_a_weak_password()
    {
        var (email, userId) = await CreateUserAsync();
        await ForgotAsync(email);
        var (_, _, token) = await WaitForLinkAsync(email, ResetSubject);

        var response = await ResetAsync(userId, token, "short");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("newPassword", out _).ShouldBeTrue();
        (await ResetAsync(userId, token)).StatusCode.ShouldBe(HttpStatusCode.NoContent); // the token was not consumed by the rejected attempt
    }

    [Fact]
    public async Task Change_password_keeps_this_device_and_signs_out_the_others()
    {
        var (email, userId) = await CreateUserAsync();
        var phone = await LoginAsync(email, deviceId: "ios-device-0001");
        var tablet = await LoginAsync(email, deviceId: "ipad-device-0002");

        (await ChangeAsync(phone.AccessToken, Password, NewPassword)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await CodeAsync(await GetMeAsync(phone.AccessToken))).ShouldBe("session_revoked"); // old stamp
        var renewed = await RefreshRawAsync(phone.RefreshToken, "ios-device-0001");
        renewed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var renewedTokens = (await renewed.Content.ReadFromJsonAsync<Tokens>())!;
        (await GetMeAsync(renewedTokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CodeAsync(await RefreshRawAsync(tablet.RefreshToken, "ipad-device-0002"))).ShouldBe("session_revoked");
        (await LoginRawAsync(email, NewPassword, "ipad-device-0002")).StatusCode.ShouldBe(HttpStatusCode.OK);
        await WaitForSubjectAsync(email, ChangedSubject);

        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AuditDbContext>().AuditLogs.AnyAsync(l => l.UserId == userId && l.Action == "auth.password_changed")).ShouldBeTrue();
    }

    [Fact]
    public async Task Change_password_rejects_a_wrong_current_password_without_logging_out()
    {
        var (email, _) = await CreateUserAsync();
        var tokens = await LoginAsync(email);

        var wrong = await ChangeAsync(tokens.AccessToken, "not my password at all", NewPassword);
        var same = await ChangeAsync(tokens.AccessToken, Password, Password);

        wrong.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(wrong)).ShouldBe("current_password_incorrect");
        same.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await GetMeAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Change_password_requires_authentication()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/me/change-password", new { currentPassword = Password, newPassword = NewPassword });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
