using System.Net.Http.Json;
using System.Text.Json;
using Astryx.Infrastructure.Jobs;
using Astryx.Modules.Strategies.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.IntegrationTests;

/// <summary>The live track record built from the scanner's real signals, and the beginner soak gate (TASKS.md T-166).</summary>
public class TrackRecordTests(TradingApiFactory factory) : IClassFixture<TradingApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await factory.PrepareAsync();
        await factory.ScanAsync(); // the fixture's design signals, e.g. the NVDA breakout
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_job_follows_every_emitted_signal_and_a_new_strategy_is_still_in_its_trial()
    {
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<TrackRecordHandler>().HandleAsync(new TrackRecordJob(), new JobContext(Guid.NewGuid(), 1, 1, null), default);
        }

        var client = await TestUsers.SignedInClientAsync(factory);
        var breakout = (await client.GetFromJsonAsync<JsonElement>("/api/v1/strategies")).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("code").GetString() == "base_breakout");
        var record = breakout.GetProperty("trackRecord");

        record.GetProperty("signals").GetInt32().ShouldBeGreaterThanOrEqualTo(1); // the fixture's NVDA breakout, at least
        record.GetProperty("soakMet").GetBoolean().ShouldBeFalse();
        record.GetProperty("soakNote").GetString()!.ShouldNotBeNullOrWhiteSpace();

        var signal = (await client.GetFromJsonAsync<JsonElement>("/api/v1/signals")).GetProperty("items").EnumerateArray().First();
        signal.GetProperty("strategy").GetProperty("soaked").GetBoolean().ShouldBeFalse(); // beginners don't see it yet
    }
}
