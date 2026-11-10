using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Astryx.Contracts.Events;
using Astryx.Contracts.Events.Identity;
using Astryx.Modules.Audit.Infrastructure.Persistence;
using Astryx.Modules.Identity.Infrastructure.Persistence;
using Astryx.Modules.Portfolio.Application;
using Astryx.Modules.Portfolio.Infrastructure.Persistence;
using Astryx.Modules.Profiles.Application;
using Astryx.Modules.Profiles.Infrastructure.Persistence;
using Astryx.Modules.Watchlists.Application;
using Astryx.Modules.Watchlists.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.IntegrationTests;

/// <summary>Registration and email verification (T-020, SYSTEM_DESIGN.md §11.3 A1–A3, §18.1).</summary>
public partial class RegistrationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private const string Password = "correct horse battery staple";

    private static string NewEmail() => $"trader-{Guid.NewGuid():N}@example.com";

    private Task<HttpResponseMessage> RegisterAsync(string email, string password = Password, string? displayName = "Alex") =>
        _client.PostAsJsonAsync("/api/v1/auth/register", new { email, password, displayName });

    private Task<HttpResponseMessage> VerifyAsync(Guid userId, string token) =>
        _client.PostAsJsonAsync("/api/v1/auth/verify-email", new { userId, token });

    private static async Task Eventually(Func<Task<bool>> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Timed out waiting for: {what}");
    }

    private async Task<(Guid UserId, string Token)> WaitForVerificationLinkAsync(string email, int expectedCount = 1)
    {
        await Eventually(() => Task.FromResult(factory.Emails.To(email).Count(m => m.Subject == "Verify your Astryx email") >= expectedCount), $"verification email #{expectedCount} to {email}");
        var message = factory.Emails.To(email).Where(m => m.Subject == "Verify your Astryx email").ElementAt(expectedCount - 1);
        var match = LinkPattern().Match(message.TextBody);
        match.Success.ShouldBeTrue(message.TextBody);
        return (Guid.Parse(match.Groups["user"].Value), match.Groups["token"].Value);
    }

    private async Task<T> InScopeAsync<TContext, T>(Func<TContext, Task<T>> query)
        where TContext : DbContext
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<TContext>());
    }

    private static async Task<string> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;

    [GeneratedRegex(@"astryx://verify-email\?userId=(?<user>[0-9a-f-]{36})&token=(?<token>[A-Za-z0-9_-]{43})")]
    private static partial Regex LinkPattern();

    [Fact]
    public async Task Register_creates_an_unverified_account_and_emails_a_verification_link()
    {
        var email = NewEmail();

        var response = await RegisterAsync(email);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await response.Content.ReadAsStringAsync()).ShouldBe("""{"emailVerificationRequired":true}""");
        var user = await InScopeAsync<IdentityDbContext, Astryx.Modules.Identity.Domain.UserAccount>(db => db.Users.AsNoTracking().SingleAsync(u => u.Email == email));
        user.EmailConfirmed.ShouldBeFalse();
        user.PasswordHash.ShouldNotContain(Password);
        user.PasswordHash.ShouldStartWith("AQAAAA"); // ASP.NET Identity v3 format (PBKDF2-HMAC-SHA512)

        var (userId, token) = await WaitForVerificationLinkAsync(email);
        userId.ShouldBe(user.Id);
        var storedHashes = await InScopeAsync<IdentityDbContext, List<byte[]>>(db => db.UserTokens.Where(t => t.UserId == user.Id).Select(t => t.TokenHash).ToListAsync());
        storedHashes.ShouldHaveSingleItem().Length.ShouldBe(32); // only the SHA-256 is stored
        Convert.ToBase64String(storedHashes[0]).ShouldNotContain(token);
    }

    [Fact]
    public async Task Registration_provisions_profile_risk_defaults_watchlist_and_paper_portfolio()
    {
        var email = NewEmail();
        await RegisterAsync(email, displayName: "Alex");
        var (userId, _) = await WaitForVerificationLinkAsync(email);

        await Eventually(async () => await InScopeAsync<PortfolioDbContext, bool>(db => db.Transactions.AnyAsync(t => db.Portfolios.Any(p => p.Id == t.PortfolioId && p.UserId == userId)))
            && await InScopeAsync<WatchlistsDbContext, bool>(db => db.Watchlists.AnyAsync(w => w.UserId == userId))
            && await InScopeAsync<ProfilesDbContext, bool>(db => db.NotificationPreferences.AnyAsync(n => n.UserId == userId)), "provisioning");

        var profile = await InScopeAsync<ProfilesDbContext, Astryx.Modules.Profiles.Domain.UserProfile>(db => db.Profiles.AsNoTracking().SingleAsync(p => p.UserId == userId));
        profile.DisplayName.ShouldBe("Alex");
        profile.Timezone.ShouldBe("America/New_York");
        profile.ScannerMinPrice.ShouldBe(10m);
        profile.ScannerMinAvgDollarVolume.ShouldBe(20_000_000m);

        var risk = await InScopeAsync<ProfilesDbContext, Astryx.Modules.Profiles.Domain.RiskSettings>(db => db.RiskSettings.AsNoTracking().SingleAsync(r => r.UserId == userId));
        risk.MaxRiskPerTradePct.ShouldBe(0.0075m);
        risk.MaxPositionWeightPct.ShouldBe(0.12m);
        risk.MaxSectorExposurePct.ShouldBe(0.35m);
        risk.SectorWarningPct.ShouldBe(0.30m);
        risk.DailyLossHaltPct.ShouldBe(0.02m);
        risk.MaxOpenPositions.ShouldBe(12);
        risk.MinRewardRisk.ShouldBe(1.5m);

        var watchlist = await InScopeAsync<WatchlistsDbContext, Astryx.Modules.Watchlists.Domain.Watchlist>(db => db.Watchlists.AsNoTracking().SingleAsync(w => w.UserId == userId));
        watchlist.IsDefault.ShouldBeTrue();
        watchlist.Name.ShouldBe("Watchlist");

        var portfolio = await InScopeAsync<PortfolioDbContext, Astryx.Modules.Portfolio.Domain.Portfolio>(db => db.Portfolios.AsNoTracking().SingleAsync(p => p.UserId == userId));
        portfolio.Mode.ShouldBe("simulated");
        portfolio.CashBalance.ShouldBe(100_000.00m);
        portfolio.StartingCash.ShouldBe(100_000.00m);
        var ledger = await InScopeAsync<PortfolioDbContext, List<Astryx.Modules.Portfolio.Domain.LedgerTransaction>>(db => db.Transactions.AsNoTracking().Where(t => t.PortfolioId == portfolio.Id).ToListAsync());
        var deposit = ledger.ShouldHaveSingleItem();
        deposit.Type.ShouldBe("initial_deposit");
        deposit.Amount.ShouldBe(100_000.00m);
        deposit.BalanceAfter.ShouldBe(portfolio.CashBalance);
    }

    [Fact]
    public async Task Verification_link_confirms_the_email_once()
    {
        var email = NewEmail();
        await RegisterAsync(email);
        var (userId, token) = await WaitForVerificationLinkAsync(email);

        (await VerifyAsync(userId, token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await InScopeAsync<IdentityDbContext, bool>(db => db.Users.Where(u => u.Id == userId).Select(u => u.EmailConfirmed).SingleAsync())).ShouldBeTrue();
        var reused = await VerifyAsync(userId, token);
        reused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(reused)).ShouldBe("invalid_or_expired_token");
    }

    [Fact]
    public async Task Duplicate_registration_looks_identical_and_emails_the_owner_instead()
    {
        var email = NewEmail();
        var first = await RegisterAsync(email);
        await WaitForVerificationLinkAsync(email);

        var second = await RegisterAsync(email.ToUpperInvariant(), password: "a different long password");

        second.StatusCode.ShouldBe(first.StatusCode);
        (await second.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
        (await InScopeAsync<IdentityDbContext, int>(db => db.Users.CountAsync(u => u.Email == email))).ShouldBe(1);
        await Eventually(() => Task.FromResult(factory.Emails.To(email).Any(m => m.Subject == "You already have an Astryx account")), "existing-account email");
        factory.Emails.To(email).Count(m => m.Subject == "Verify your Astryx email").ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_registrations_of_one_email_create_one_account()
    {
        var email = NewEmail();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => RegisterAsync(email)));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created);
        (await InScopeAsync<IdentityDbContext, int>(db => db.Users.CountAsync(u => u.Email == email))).ShouldBe(1);
    }

    [Fact]
    public async Task Expired_or_wrong_tokens_are_rejected()
    {
        var email = NewEmail();
        await RegisterAsync(email);
        var (userId, token) = await WaitForVerificationLinkAsync(email);

        (await CodeAsync(await VerifyAsync(userId, new string('A', 43)))).ShouldBe("invalid_or_expired_token");
        (await CodeAsync(await VerifyAsync(Guid.NewGuid(), token))).ShouldBe("invalid_or_expired_token");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database
                .ExecuteSqlAsync($"UPDATE identity.user_tokens SET expires_at = now() - interval '1 minute' WHERE user_id = {userId}");
        }

        (await CodeAsync(await VerifyAsync(userId, token))).ShouldBe("invalid_or_expired_token");
    }

    [Fact]
    public async Task Resend_issues_a_new_link_and_invalidates_the_old_one()
    {
        var email = NewEmail();
        await RegisterAsync(email);
        var (userId, oldToken) = await WaitForVerificationLinkAsync(email);

        (await _client.PostAsJsonAsync("/api/v1/auth/resend-verification", new { email })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var (_, newToken) = await WaitForVerificationLinkAsync(email, expectedCount: 2);

        newToken.ShouldNotBe(oldToken);
        (await CodeAsync(await VerifyAsync(userId, oldToken))).ShouldBe("invalid_or_expired_token");
        (await VerifyAsync(userId, newToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Resend_reveals_nothing_for_unknown_or_verified_emails()
    {
        var unknown = NewEmail();
        var verified = NewEmail();
        await RegisterAsync(verified);
        var (userId, token) = await WaitForVerificationLinkAsync(verified);
        await VerifyAsync(userId, token);

        var unknownResponse = await _client.PostAsJsonAsync("/api/v1/auth/resend-verification", new { email = unknown });
        var verifiedResponse = await _client.PostAsJsonAsync("/api/v1/auth/resend-verification", new { email = verified });
        await Task.Delay(1500); // give the outbox a chance to (wrongly) send something

        unknownResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        verifiedResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        factory.Emails.To(unknown).ShouldBeEmpty();
        factory.Emails.To(verified).Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("not-an-email", Password, "email")]
    [InlineData("trader@example.com", "too-short", "password")]
    [InlineData("trader@localhost", Password, "email")]
    public async Task Invalid_input_returns_field_errors(string email, string password, string field)
    {
        var response = await RegisterAsync(email, password);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Email_outage_does_not_fail_registration_and_the_email_is_retried()
    {
        var email = NewEmail();
        factory.Emails.FailuresRemaining = 1;

        var response = await RegisterAsync(email);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        await WaitForVerificationLinkAsync(email); // delivered by the outbox retry
    }

    [Fact]
    public async Task Provisioning_handlers_are_idempotent()
    {
        var email = NewEmail();
        await RegisterAsync(email);
        var (userId, _) = await WaitForVerificationLinkAsync(email);
        await Eventually(() => InScopeAsync<PortfolioDbContext, bool>(db => db.Portfolios.AnyAsync(p => p.UserId == userId)), "portfolio");
        await Eventually(() => InScopeAsync<WatchlistsDbContext, bool>(db => db.Watchlists.AnyAsync(w => w.UserId == userId)), "watchlist");
        await Eventually(() => InScopeAsync<ProfilesDbContext, bool>(db => db.NotificationPreferences.AnyAsync(n => n.UserId == userId)), "profile");

        var redelivery = new UserRegistered(userId, "Alex");
        var context = new IntegrationEventContext(Guid.NewGuid(), DateTimeOffset.UtcNow, null);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            await services.GetRequiredService<ProvisionProfileHandler>().HandleAsync(redelivery, context, CancellationToken.None);
            await services.GetRequiredService<CreateDefaultWatchlistHandler>().HandleAsync(redelivery, context, CancellationToken.None);
            await services.GetRequiredService<CreatePaperPortfolioHandler>().HandleAsync(redelivery, context, CancellationToken.None);
        }

        (await InScopeAsync<ProfilesDbContext, int>(db => db.RiskSettings.CountAsync(r => r.UserId == userId))).ShouldBe(1);
        (await InScopeAsync<WatchlistsDbContext, int>(db => db.Watchlists.CountAsync(w => w.UserId == userId))).ShouldBe(1);
        (await InScopeAsync<PortfolioDbContext, int>(db => db.Portfolios.CountAsync(p => p.UserId == userId))).ShouldBe(1);
        (await InScopeAsync<PortfolioDbContext, int>(db => db.Transactions.CountAsync(t => db.Portfolios.Any(p => p.Id == t.PortfolioId && p.UserId == userId)))).ShouldBe(1);
    }

    [Fact]
    public async Task Registration_and_verification_are_audited_with_masked_email()
    {
        var email = NewEmail();
        await RegisterAsync(email);
        var (userId, token) = await WaitForVerificationLinkAsync(email);
        await VerifyAsync(userId, token);

        var actions = await InScopeAsync<AuditDbContext, List<(string Action, string? After)>>(async db =>
            (await db.AuditLogs.AsNoTracking().Where(a => a.UserId == userId).OrderBy(a => a.OccurredAt).Select(a => new { a.Action, a.After }).ToListAsync())
            .Select(a => (a.Action, a.After)).ToList());

        actions.Select(a => a.Action).ShouldBe(["auth.registered", "auth.email_verified"]);
        actions[0].After!.ShouldNotContain(email);
        actions[0].After!.ShouldContain("t***@example.com");
    }
}
