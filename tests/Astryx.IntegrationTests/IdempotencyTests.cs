using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Astryx.Api.TestSupport;
using Astryx.Infrastructure.Idempotency;
using Astryx.Infrastructure.Jobs;
using Astryx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astryx.IntegrationTests;

/// <summary>Idempotency-Key handling (T-016, SYSTEM_DESIGN.md §11.1, §20.10, §21.7).</summary>
public class IdempotencyTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();
    private TestSupportEndpoints.TestOrderCounter Counter => factory.Services.GetRequiredService<TestSupportEndpoints.TestOrderCounter>();

    public async Task InitializeAsync()
    {
        _ = Counter; // host started
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<InfraDbContext>().Database.ExecuteSqlRawAsync("TRUNCATE infra.idempotency_records");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<HttpResponseMessage> PostAsync(string? key, string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/_test/orders") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return _client.SendAsync(request);
    }

    private static string NewKey() => Guid.NewGuid().ToString();

    private static async Task<string> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;

    [Fact]
    public async Task IDEM_01_sequential_retry_replays_the_original_response()
    {
        var key = NewKey();
        var before = Counter.Count;
        const string body = """{"symbol":"NVDA","quantity":"76"}""";

        var first = await PostAsync(key, body);
        var second = await PostAsync(key, body);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        first.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
        second.Headers.GetValues("Idempotent-Replayed").Single().ShouldBe("true");
        (await second.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
        second.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        Counter.Count.ShouldBe(before + 1);
    }

    [Fact]
    public async Task IDEM_02_ten_concurrent_requests_execute_once()
    {
        var key = NewKey();
        var before = Counter.Count;
        const string body = """{"symbol":"NVDA","quantity":"76","delayMs":400}""";

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => PostAsync(key, body)));

        Counter.Count.ShouldBe(before + 1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Created && !r.Headers.Contains("Idempotent-Replayed")).ShouldBe(1);
        foreach (var other in responses.Where(r => r.Headers.Contains("Idempotent-Replayed") || r.StatusCode != HttpStatusCode.Created))
        {
            if (other.StatusCode == HttpStatusCode.Conflict)
            {
                (await CodeAsync(other)).ShouldBe("request_in_progress");
            }
            else
            {
                other.StatusCode.ShouldBe(HttpStatusCode.Created);
            }
        }

        var afterwards = await PostAsync(key, body);
        afterwards.StatusCode.ShouldBe(HttpStatusCode.Created);
        afterwards.Headers.GetValues("Idempotent-Replayed").Single().ShouldBe("true");
        Counter.Count.ShouldBe(before + 1);
    }

    [Fact]
    public async Task IDEM_03_same_key_with_a_different_body_is_rejected()
    {
        var key = NewKey();
        await PostAsync(key, """{"symbol":"NVDA","quantity":"76"}""");

        var reused = await PostAsync(key, """{"symbol":"NVDA","quantity":"80"}""");

        reused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeAsync(reused)).ShouldBe("idempotency_key_reused");
    }

    [Fact]
    public async Task Different_keys_execute_separately()
    {
        var before = Counter.Count;
        const string body = """{"symbol":"NVDA","quantity":"76"}""";

        await PostAsync(NewKey(), body);
        await PostAsync(NewKey(), body);

        Counter.Count.ShouldBe(before + 2);
    }

    [Theory]
    [InlineData(null, "idempotency_key_required")]
    [InlineData("short", "idempotency_key_invalid")]
    [InlineData("has spaces in it!", "idempotency_key_invalid")]
    public async Task Missing_or_malformed_key_is_rejected(string? key, string code)
    {
        var before = Counter.Count;

        var response = await PostAsync(key, """{"symbol":"NVDA","quantity":"76"}""");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).ShouldBe(code);
        Counter.Count.ShouldBe(before);
    }

    [Fact]
    public async Task Server_errors_are_not_stored_so_a_retry_runs_again()
    {
        var key = NewKey();
        var before = Counter.Count;
        const string body = """{"symbol":"NVDA","quantity":"76"}""";
        Counter.FailuresRemaining = 1;

        var failed = await PostAsync(key, body);
        var retried = await PostAsync(key, body);

        failed.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        retried.StatusCode.ShouldBe(HttpStatusCode.Created);
        retried.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
        Counter.Count.ShouldBe(before + 1);
    }

    [Fact]
    public async Task Abandoned_in_progress_request_can_be_taken_over()
    {
        var key = NewKey();
        const string body = """{"symbol":"NVDA","quantity":"76"}""";
        var first = await PostAsync(key, body); // learn the stored hash for this exact request
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<InfraDbContext>().Database.ExecuteSqlAsync($"""
                UPDATE infra.idempotency_records
                SET status = 'in_progress', response_status = NULL, response_body = NULL, in_progress_until = now() - interval '1 minute'
                WHERE key = {key}
                """);
        }

        var before = Counter.Count;
        var takeover = await PostAsync(key, body);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        takeover.StatusCode.ShouldBe(HttpStatusCode.Created);
        takeover.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
        Counter.Count.ShouldBe(before + 1);
    }

    [Fact]
    public async Task Expired_key_can_be_used_again()
    {
        var key = NewKey();
        await PostAsync(key, """{"symbol":"NVDA","quantity":"76"}""");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<InfraDbContext>().Database
                .ExecuteSqlAsync($"UPDATE infra.idempotency_records SET expires_at = now() - interval '1 second' WHERE key = {key}");
        }

        var reused = await PostAsync(key, """{"symbol":"AMD","quantity":"10"}""");

        reused.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Cleanup_job_deletes_only_expired_records()
    {
        await PostAsync(NewKey(), """{"symbol":"NVDA","quantity":"1"}""");
        var expiredKey = NewKey();
        await PostAsync(expiredKey, """{"symbol":"NVDA","quantity":"2"}""");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraDbContext>();
        await db.Database.ExecuteSqlAsync($"UPDATE infra.idempotency_records SET expires_at = now() - interval '1 hour' WHERE key = {expiredKey}");

        var handler = new IdempotencyCleanupHandler(db, TimeProvider.System, NullLogger<IdempotencyCleanupHandler>.Instance);
        await handler.HandleAsync(new IdempotencyCleanupJob(), new JobContext(Guid.NewGuid(), 1, 3, null), CancellationToken.None);

        var keys = await db.IdempotencyRecords.AsNoTracking().Select(r => r.Key).ToListAsync();
        keys.Count.ShouldBe(1);
        keys.ShouldNotContain(expiredKey);
    }

    [Fact]
    public void Cleanup_runs_as_an_hourly_recurring_job()
    {
        var registry = factory.Services.GetRequiredService<JobRegistry>();

        var schedule = registry.Recurring.Single(r => r.Name == "idempotency-cleanup");
        schedule.Interval.ShouldBe(TimeSpan.FromHours(1));
        registry.HandlerFor(IdempotencyCleanupJob.JobType).ShouldNotBeNull();
    }
}
