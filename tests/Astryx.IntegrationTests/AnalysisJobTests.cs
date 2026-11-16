using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astryx.Infrastructure.Jobs;
using Astryx.Modules.Ai.Application.Jobs;
using Astryx.Modules.Ai.Domain;
using Astryx.Modules.Ai.Infrastructure.Fake;
using Astryx.Modules.Ai.Infrastructure.Persistence;
using Astryx.Modules.Discovery.Application.Scanner;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.IntegrationTests;

/// <summary>Synced fixture market with the scripted <see cref="FakeAiProvider"/> instead of Ollama.</summary>
public sealed class FakeAiApiFactory : SyncedMarketApiFactory
{
    protected override IReadOnlyDictionary<string, string> ExtraSettings => new Dictionary<string, string> { ["Ai:Provider"] = "Fake" };

    public FakeAiProvider Fake => Services.GetRequiredService<FakeAiProvider>();
}

/// <summary>Shared steps for analysis job tests: request, run the worker step, inspect rows.</summary>
public static class AnalysisSteps
{
    public static async Task PrepareAsync(SyncedMarketApiFactory factory)
    {
        await factory.EnsureSyncedAsync();
        await factory.RefreshQuotesAsync(); // the context builder refuses stale quotes
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ScannerRunHandler>().HandleAsync(new ScannerRunJob("manual"), new JobContext(Guid.NewGuid(), 1, 1, null), default);
    }

    public static Task<HttpResponseMessage> RequestAsync(HttpClient client, string symbol = "NVDA", string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/analyses") { Content = JsonContent.Create(new { symbol }) };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return client.SendAsync(request);
    }

    /// <summary>Runs the worker step until nothing is due. Returns how many jobs it processed.</summary>
    public static async Task<int> DrainAsync(SyncedMarketApiFactory factory, string worker = "test-worker")
    {
        var processor = factory.Services.GetRequiredService<AnalysisJobProcessor>();
        var count = 0;
        while (count < 50 && await processor.RunOnceAsync(worker, default))
        {
            count++;
        }

        return count;
    }

    public static async Task<T> QueryAsync<T>(SyncedMarketApiFactory factory, Func<AiDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AiDbContext>());
    }

    /// <summary>Makes a job waiting on backoff due now.</summary>
    public static Task MakeDueAsync(SyncedMarketApiFactory factory, Guid jobId) =>
        QueryAsync(factory, db => db.Jobs.Where(j => j.Id == jobId).ExecuteUpdateAsync(s => s.SetProperty(j => j.RunAfter, DateTimeOffset.UtcNow.AddSeconds(-1))));
}

/// <summary>AI1–AI5, the worker and the failure paths (SYSTEM_DESIGN.md §11.4, §12.8, §21.8, TASKS.md T-073).</summary>
public class AnalysisJobTests(FakeAiApiFactory factory) : IClassFixture<FakeAiApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await AnalysisSteps.PrepareAsync(factory);
        await AnalysisSteps.DrainAsync(factory); // leftovers from earlier tests must not eat this test's script
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task A_request_is_accepted_at_once_processed_by_the_worker_and_served_as_a_result()
    {
        var client = await TestUsers.SignedInClientAsync(factory);

        var response = await AnalysisSteps.RequestAsync(client);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var job = await JsonAsync(response);
        var jobId = job.GetProperty("id").GetGuid();
        response.Headers.Location!.ToString().ShouldBe($"/api/v1/analyses/jobs/{jobId}");
        (job.GetProperty("status").GetString(), job.GetProperty("pollAfterMs").GetInt32()).ShouldBe(("pending", 2000));

        (await AnalysisSteps.DrainAsync(factory)).ShouldBe(1);

        var done = await client.GetFromJsonAsync<JsonElement>($"/api/v1/analyses/jobs/{jobId}");
        (done.GetProperty("status").GetString(), done.GetProperty("attempt").GetInt32()).ShouldBe(("completed", 1));
        done.GetProperty("pollAfterMs").ValueKind.ShouldBe(JsonValueKind.Null);
        var resultId = done.GetProperty("resultId").GetGuid();

        var result = await client.GetFromJsonAsync<JsonElement>($"/api/v1/analyses/{resultId}");
        result.GetProperty("symbol").GetString().ShouldBe("NVDA");
        result.GetProperty("validationStatus").GetString().ShouldBeOneOf("valid", "valid_with_rejections");
        result.GetProperty("overallSentiment").GetString().ShouldBe("bullish");
        result.GetProperty("rewardRisk").GetString().ShouldBe("1.77");
        result.GetProperty("levelValidation").GetProperty("accepted").GetBoolean().ShouldBeTrue();
        result.GetProperty("suggestedLevels").GetProperty("stopLoss").GetString().ShouldBe("178.20");
        result.GetProperty("stale").GetBoolean().ShouldBeFalse();
        (result.GetProperty("model").GetString(), result.GetProperty("promptVersion").GetString()).ShouldBe((FakeAiProvider.ModelName, "stock_analysis/2026-11-09.1"));
        result.GetProperty("disclaimer").GetString()!.ShouldContain("Not investment advice");
        result.GetProperty("confidence").GetProperty("value").GetString().ShouldBe("0.55"); // decimals travel as strings

        var page = await client.GetFromJsonAsync<JsonElement>("/api/v1/analyses?symbol=nvda");
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ShouldBe([resultId]);

        var stored = await AnalysisSteps.QueryAsync(factory, db => db.Results.SingleAsync(r => r.Id == resultId));
        JsonDocument.Parse(stored.Context).RootElement.GetProperty("contextVersion").GetString().ShouldBe("ctx.v1");
        (stored.Provider, stored.ModelDigest, stored.OutputSchemaVersion, stored.RewardRisk).ShouldBe(("fake", FakeAiProvider.Digest, "analysis.v1", 1.77m));
        var requests = await AnalysisSteps.QueryAsync(factory, db => db.Requests.Where(r => r.JobId == jobId).ToListAsync());
        requests.Single().ShouldSatisfyAllConditions(r => r.Outcome.ShouldBe("ok"), r => r.PromptTokens.ShouldBe(812), r => r.CompletionTokens.ShouldBe(640), r => r.ErrorMessage.ShouldBeNull());
    }

    [Fact]
    public async Task The_same_question_returns_the_existing_job()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        var first = await JsonAsync(await AnalysisSteps.RequestAsync(client, key: "analysis-key-1"));

        var replay = await AnalysisSteps.RequestAsync(client, key: "analysis-key-1");
        var equivalent = await AnalysisSteps.RequestAsync(client, symbol: "nvda", key: "analysis-key-2");

        (await JsonAsync(replay)).GetProperty("id").GetGuid().ShouldBe(first.GetProperty("id").GetGuid());
        equivalent.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonAsync(equivalent)).GetProperty("id").GetGuid().ShouldBe(first.GetProperty("id").GetGuid());
        await AnalysisSteps.DrainAsync(factory);

        var afterCompletion = await AnalysisSteps.RequestAsync(client, key: "analysis-key-3"); // completed < 15 min ago: still the same answer
        afterCompletion.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await JsonAsync(afterCompletion)).GetProperty("status").GetString().ShouldBe("completed");
    }

    [Fact]
    public async Task Another_active_job_for_the_symbol_is_a_409_with_its_id()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        var job = await JsonAsync(await AnalysisSteps.RequestAsync(client));
        var jobId = job.GetProperty("id").GetGuid();
        await AnalysisSteps.QueryAsync(factory, db => db.Jobs.Where(j => j.Id == jobId).ExecuteUpdateAsync(s => s.SetProperty(j => j.RequestHash, "older-data")));

        var response = await AnalysisSteps.RequestAsync(client);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await JsonAsync(response);
        (problem.GetProperty("code").GetString(), problem.GetProperty("jobId").GetGuid()).ShouldBe(("duplicate", jobId));
        await AnalysisSteps.DrainAsync(factory);
    }

    [Fact]
    public async Task The_daily_quota_is_thirty()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        var userId = me.GetProperty("id").GetGuid();
        await AnalysisSteps.QueryAsync(factory, async db =>
        {
            for (var i = 0; i < 30; i++)
            {
                db.Jobs.Add(new AnalysisJob
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    InstrumentId = Guid.NewGuid(),
                    Symbol = "OLD",
                    Status = AnalysisJobStatus.Completed,
                    IdempotencyKey = $"old-{i}",
                    RequestHash = $"old-{i}",
                    CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
                    RunAfter = DateTimeOffset.UtcNow,
                });
            }

            return await db.SaveChangesAsync();
        });

        var response = await AnalysisSteps.RequestAsync(client);

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await JsonAsync(response)).GetProperty("code").GetString().ShouldBe("ai_quota_exceeded");
    }

    [Fact]
    public async Task Unknown_symbols_and_kinds_are_rejected_up_front()
    {
        var client = await TestUsers.SignedInClientAsync(factory);

        (await AnalysisSteps.RequestAsync(client, symbol: "ZZZZZ")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/analyses") { Content = JsonContent.Create(new { symbol = "NVDA", kind = "signal_explanation" }) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        (await client.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var noKey = await client.PostAsJsonAsync("/api/v1/analyses", new { symbol = "NVDA" });
        noKey.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await JsonAsync(noKey)).GetProperty("code").GetString().ShouldBe("idempotency_key_required");
    }

    [Fact]
    public async Task Cancelling_a_pending_job_ends_it_and_a_processing_job_stops_at_the_checkpoint()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        var pending = (await JsonAsync(await AnalysisSteps.RequestAsync(client))).GetProperty("id").GetGuid();

        var cancel = await client.DeleteAsync($"/api/v1/analyses/jobs/{pending}");

        cancel.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await JsonAsync(cancel)).GetProperty("status").GetString().ShouldBe("cancelled");
        (await client.DeleteAsync($"/api/v1/analyses/jobs/{pending}")).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var processing = (await JsonAsync(await AnalysisSteps.RequestAsync(client, symbol: "AAPL"))).GetProperty("id").GetGuid();
        var processor = factory.Services.GetRequiredService<AnalysisJobProcessor>();
        var claimed = await processor.ClaimAsync("test-worker", default);
        claimed!.Id.ShouldBe(processing);
        (await JsonAsync(await client.DeleteAsync($"/api/v1/analyses/jobs/{processing}"))).GetProperty("status").GetString().ShouldBe("processing");

        await processor.ProcessAsync(claimed, "test-worker", default);

        var job = await client.GetFromJsonAsync<JsonElement>($"/api/v1/analyses/jobs/{processing}");
        (job.GetProperty("status").GetString(), job.GetProperty("errorCode").GetString()).ShouldBe(("cancelled", "cancelled"));
        (await AnalysisSteps.QueryAsync(factory, db => db.Results.CountAsync(r => r.JobId == processing))).ShouldBe(0);
    }

    [Fact]
    public async Task Garbage_gets_one_repair_retry_then_fails_with_the_raw_output_kept()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        factory.Fake.Enqueue(FakeAiScenario.InvalidJson, FakeAiScenario.InvalidJson);
        var jobId = (await JsonAsync(await AnalysisSteps.RequestAsync(client))).GetProperty("id").GetGuid();

        await AnalysisSteps.DrainAsync(factory);

        var job = await client.GetFromJsonAsync<JsonElement>($"/api/v1/analyses/jobs/{jobId}");
        (job.GetProperty("status").GetString(), job.GetProperty("errorCode").GetString()).ShouldBe(("failed", "ai_output_invalid"));
        job.GetProperty("errorDetail").GetString().ShouldBe("Analysis couldn't be generated reliably. Indicators are still available.");
        job.GetProperty("resultId").ValueKind.ShouldBe(JsonValueKind.Null);
        var stored = await AnalysisSteps.QueryAsync(factory, db => db.Results.SingleAsync(r => r.JobId == jobId));
        (stored.ValidationStatus, stored.Parsed).ShouldBe(("invalid", null));
        stored.RawOutput.ShouldBe(FakeAiProvider.Fixture(FakeAiScenario.InvalidJson));
        (await AnalysisSteps.QueryAsync(factory, db => db.Requests.Where(r => r.JobId == jobId).Select(r => r.Outcome).ToListAsync())).ShouldBe(["parse_error", "parse_error"]);
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/analyses")).GetProperty("items").GetArrayLength().ShouldBe(0);
        (await client.GetAsync($"/api/v1/analyses/{stored.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Provider_errors_retry_with_backoff_then_fail_as_unavailable()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        factory.Fake.Enqueue(FakeAiScenario.ServerError, FakeAiScenario.ServerError, FakeAiScenario.ServerError);
        var jobId = (await JsonAsync(await AnalysisSteps.RequestAsync(client))).GetProperty("id").GetGuid();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            (await AnalysisSteps.DrainAsync(factory)).ShouldBe(1);
            var job = await AnalysisSteps.QueryAsync(factory, db => db.Jobs.SingleAsync(j => j.Id == jobId));
            job.Attempt.ShouldBe(attempt);
            if (attempt < 3)
            {
                job.Status.ShouldBe("pending");
                job.RunAfter.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddSeconds(attempt == 1 ? 7 : 23)); // 10 s, then 30 s, ±20 %
                await AnalysisSteps.MakeDueAsync(factory, jobId);
            }
            else
            {
                (job.Status, job.ErrorCode).ShouldBe(("failed", "ai_provider_unavailable"));
            }
        }

        (await AnalysisSteps.QueryAsync(factory, db => db.Requests.Where(r => r.JobId == jobId).Select(r => new { r.Outcome, r.HttpStatus }).ToListAsync()))
            .ShouldAllBe(r => r.Outcome == "http_error" && r.HttpStatus == 500);
    }

    [Fact]
    public async Task A_worker_killed_mid_job_loses_its_lease_and_the_job_completes_once_on_attempt_two()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        var jobId = (await JsonAsync(await AnalysisSteps.RequestAsync(client))).GetProperty("id").GetGuid();
        var processor = factory.Services.GetRequiredService<AnalysisJobProcessor>();
        var dead = await processor.ClaimAsync("worker-that-dies", default);
        dead!.Id.ShouldBe(jobId);
        await AnalysisSteps.QueryAsync(factory, db => db.Jobs.Where(j => j.Id == jobId).ExecuteUpdateAsync(s => s.SetProperty(j => j.LockedUntil, DateTimeOffset.UtcNow.AddSeconds(-1))));

        (await AnalysisSteps.DrainAsync(factory, "worker-2")).ShouldBe(1);
        await processor.ProcessAsync(dead, "worker-that-dies", default); // the zombie wakes up and tries to settle

        var job = await AnalysisSteps.QueryAsync(factory, db => db.Jobs.SingleAsync(j => j.Id == jobId));
        (job.Status, job.Attempt).ShouldBe(("completed", 2));
        (await AnalysisSteps.QueryAsync(factory, db => db.Results.CountAsync(r => r.JobId == jobId))).ShouldBe(1);
        job.ResultId.ShouldBe(await AnalysisSteps.QueryAsync(factory, db => db.Results.Where(r => r.JobId == jobId).Select(r => (Guid?)r.Id).SingleAsync()));
    }

    [Fact]
    public async Task A_lease_that_expires_on_the_last_attempt_fails_the_job()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        var jobId = (await JsonAsync(await AnalysisSteps.RequestAsync(client))).GetProperty("id").GetGuid();
        await AnalysisSteps.QueryAsync(factory, db => db.Jobs.Where(j => j.Id == jobId).ExecuteUpdateAsync(s => s
            .SetProperty(j => j.Status, "processing").SetProperty(j => j.Attempt, 3).SetProperty(j => j.LockedBy, "gone").SetProperty(j => j.LockedUntil, DateTimeOffset.UtcNow.AddSeconds(-1))));

        await AnalysisSteps.DrainAsync(factory);

        var job = await AnalysisSteps.QueryAsync(factory, db => db.Jobs.SingleAsync(j => j.Id == jobId));
        (job.Status, job.ErrorCode).ShouldBe(("failed", "lease_expired"));
    }
}

/// <summary>§21.8 "Ollama stopped": the real provider against a closed port. Indicators still render.</summary>
public class AnalysisOllamaStoppedTests(SyncedMarketApiFactory factory) : IClassFixture<SyncedMarketApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => AnalysisSteps.PrepareAsync(factory);

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Jobs_fail_after_three_attempts_with_ai_provider_unavailable()
    {
        var client = await TestUsers.SignedInClientAsync(factory);
        var jobId = (await (await AnalysisSteps.RequestAsync(client)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            (await AnalysisSteps.DrainAsync(factory)).ShouldBe(1);
            await AnalysisSteps.MakeDueAsync(factory, jobId);
        }

        var job = await client.GetFromJsonAsync<JsonElement>($"/api/v1/analyses/jobs/{jobId}");
        (job.GetProperty("status").GetString(), job.GetProperty("errorCode").GetString(), job.GetProperty("attempt").GetInt32()).ShouldBe(("failed", "ai_provider_unavailable", 3));
        job.GetProperty("errorDetail").GetString().ShouldBe("The local AI model is unavailable. Indicators are still available.");
        (await client.GetAsync("/api/v1/market/NVDA/indicators")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
