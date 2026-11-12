using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Astryx.IntegrationTests;

/// <summary>Registers, verifies and signs in a fresh user through the real auth endpoints.</summary>
public static partial class TestUsers
{
    public const string Password = "correct horse battery staple";

    [GeneratedRegex(@"userId=(?<user>[0-9a-f-]{36})&token=(?<token>[A-Za-z0-9_-]{43})")]
    private static partial Regex LinkPattern();

    /// <summary>
    /// Verifies an email. The verification email is sent before its token commits (so a failed send stores nothing),
    /// which means a test can read the email a moment before the token is visible; retry briefly on 422.
    /// </summary>
    public static async Task VerifyEmailAsync(HttpClient client, Guid userId, string token)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            var response = await client.PostAsJsonAsync("/api/v1/auth/verify-email", new { userId, token });
            if (response.StatusCode != System.Net.HttpStatusCode.UnprocessableEntity || DateTime.UtcNow > deadline)
            {
                response.EnsureSuccessStatusCode();
                return;
            }

            await Task.Delay(50);
        }
    }

    /// <summary>An <see cref="HttpClient"/> that sends a fresh user's bearer token. Needs the outbox running (real clock).</summary>
    public static async Task<HttpClient> SignedInClientAsync(ApiFactory factory)
    {
        var client = factory.CreateClient();
        var email = $"user-{Guid.NewGuid():N}@example.com";
        (await client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = Password })).EnsureSuccessStatusCode();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!factory.Emails.To(email).Any() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        var match = LinkPattern().Match(factory.Emails.To(email).Single().TextBody);
        await VerifyEmailAsync(client, Guid.Parse(match.Groups["user"].Value), match.Groups["token"].Value);
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password, deviceId = "ios-device-0001" });
        var accessToken = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }
}
