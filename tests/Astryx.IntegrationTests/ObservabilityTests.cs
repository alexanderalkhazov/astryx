using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astryx.Infrastructure.Observability;
using Astryx.Infrastructure.Outbox;
using Astryx.Modules.Notifications.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.IntegrationTests;

/// <summary>Correlation IDs through the outbox and jobs, and the domain metrics (SYSTEM_DESIGN.md §23, TASKS.md T-113).</summary>
public class ObservabilityTests(PushApiFactory factory) : IClassFixture<PushApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await factory.PrepareAsync();
        await factory.ScanAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task One_request_correlation_id_reaches_its_outbox_events_and_the_jobs_their_handlers_enqueue()
    {
        var measurements = new ConcurrentBag<(string Instrument, long Value, Dictionary<string, object?> Tags)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == AstryxTelemetry.Name)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, tags.ToArray().ToDictionary(t => t.Key, t => t.Value))));
        listener.Start();

        var client = await TestUsers.SignedInClientAsync(factory);
        (await client.PutAsJsonAsync("/api/v1/me/settings/notifications", new { fills = true, signals = true, stopProximity = true, strategyGuard = true, system = true, dailySummary = false, pushEnabled = true }))
            .EnsureSuccessStatusCode();
        await factory.MakeQuoteLiveAsync("NVDA", 186.60m);
        var proposal = await (await client.PostAsJsonAsync("/api/v1/proposals", new { source = "signal", signalId = await factory.ActiveSignalIdAsync("NVDA") })).Content.ReadFromJsonAsync<JsonElement>();
        var correlation = $"corr-{Guid.NewGuid():N}";
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/paper/orders")
        {
            Content = JsonContent.Create(new { proposalId = proposal.GetProperty("id").GetGuid(), assessmentId = proposal.GetProperty("assessment").GetProperty("id").GetGuid() }),
        };
        request.Headers.Add("Idempotency-Key", $"key-{Guid.NewGuid():N}");
        request.Headers.Add("X-Correlation-Id", correlation);

        (await client.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var dispatcher = factory.Services.GetRequiredService<OutboxDispatcher>();
        while (await dispatcher.DispatchOnceAsync(default) > 0)
        {
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var outbox = await db.Database.SqlQuery<string>($"SELECT type AS \"Value\" FROM infra.outbox_messages WHERE correlation_id = {correlation}").ToListAsync();
        outbox.ShouldContain("paper.order_filled.v1"); // written in the request
        var jobs = await db.Database.SqlQuery<string>($"SELECT type AS \"Value\" FROM infra.job_queue WHERE correlation_id = {correlation}").ToListAsync();
        jobs.ShouldContain("notifications.push.v1"); // enqueued by an outbox handler, under the request's id

        measurements.ShouldContain(m => m.Instrument == "orders_total" && (string?)m.Tags["outcome"] == "filled");
        measurements.ShouldContain(m => m.Instrument == "risk_assessments_total" && (bool?)m.Tags["approvable"] == true);
    }
}
