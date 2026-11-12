using System.Net.Http.Json;
using System.Text.Json;
using Astryx.Contracts.MarketData;
using Astryx.Infrastructure.Jobs;
using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Discovery.Application.Scanner;
using Astryx.Modules.Discovery.Domain;
using Astryx.Modules.Discovery.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.IntegrationTests;

/// <summary>Scanner runs and signals (SYSTEM_DESIGN.md §7.4, §19.2, T-065).</summary>
public class ScannerTests(SyncedMarketApiFactory factory) : IClassFixture<SyncedMarketApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.EnsureSyncedAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task RunAsync(string trigger = "manual")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ScannerRunHandler>().HandleAsync(new ScannerRunJob(trigger), new JobContext(Guid.NewGuid(), 1, 1, null), default);
    }

    private async Task<T> QueryAsync<T>(Func<DiscoveryDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<DiscoveryDbContext>());
    }

    [Fact]
    public async Task A_run_finds_the_design_signal_on_NVDA_and_supersedes_it_on_the_next_run()
    {
        await RunAsync();

        var run = await QueryAsync(db => db.ScannerRuns.OrderByDescending(r => r.StartedAt).FirstAsync());
        run.Status.ShouldBeOneOf(ScannerRunStatus.Completed, ScannerRunStatus.CompletedWithWarnings);
        run.Trigger.ShouldBe("manual");
        run.SymbolsTotal.ShouldBe(1031); // equities only: SPY, QQQ and IWM are ETFs
        run.SymbolsEvaluated.ShouldBeGreaterThan(0);
        run.SymbolsEvaluated.ShouldBeLessThan(run.SymbolsTotal - run.SymbolsSkipped); // the $10 · $20M filters drop some
        run.DataAsOf.ShouldNotBeNull();
        run.FinishedAt.ShouldNotBeNull();

        var nvda = await QueryAsync(db => db.Signals.SingleAsync(s => s.Symbol == "NVDA" && s.ScannerRunId == run.Id));
        (nvda.SignalType, nvda.OrderType, nvda.EntryPrice, nvda.StopPrice, nvda.TargetPrice, nvda.Score).ShouldBe(("breakout", "buy_stop", 186.50m, 178.20m, 204.00m, 87m));
        nvda.Reason.ShouldBe("Cleared a 6-week base at $185.90 on 2.4× average volume.");
        nvda.StrategyName.ShouldBe("Base Breakout v3");
        nvda.Status.ShouldBe(SignalStatus.Active);
        nvda.ExpiresAt.ShouldBeGreaterThan(nvda.EvaluatedAt);
        var breakdown = JsonDocument.Parse(nvda.ScoreBreakdown).RootElement.EnumerateArray().ToList();
        breakdown.Select(b => b.GetProperty("component").GetString()).ShouldBe(["setup", "relativeStrength", "volume", "trend", "rewardRisk"]);
        breakdown.Single(b => b.GetProperty("component").GetString() == "volume").GetProperty("value").GetString().ShouldBe("0.8"); // 2.4 / 3
        var metrics = JsonDocument.Parse(nvda.Metrics).RootElement;
        metrics.GetProperty("relVolume").GetString().ShouldBe("2.40");
        metrics.GetProperty("rewardRisk").GetString().ShouldBe("2.11");

        await RunAsync();

        var signals = await QueryAsync(db => db.Signals.Where(s => s.Symbol == "NVDA").OrderBy(s => s.EvaluatedAt).ToListAsync());
        signals.Count.ShouldBe(2);
        signals[0].Status.ShouldBe(SignalStatus.Superseded);
        signals[1].Status.ShouldBe(SignalStatus.Active);

        await using var scope = factory.Services.CreateAsyncScope();
        var infra = scope.ServiceProvider.GetRequiredService<InfraDbContext>();
        var messages = await infra.OutboxMessages.Where(m => m.Type == "discovery.signal_detected.v1").Select(m => m.Payload).ToListAsync();
        var detected = messages.Count(p => p.Contains("\"NVDA\"", StringComparison.Ordinal));
        detected.ShouldBe(1); // the second run re-confirms; it doesn't announce again
    }

    [Fact]
    public async Task Active_signals_feed_chart_levels_and_quote_priority()
    {
        await RunAsync();
        var client = await TestUsers.SignedInClientAsync(factory);

        var bars = await client.GetFromJsonAsync<JsonElement>("/api/v1/market/NVDA/bars?timeframe=3M");

        var levels = bars.GetProperty("levels");
        (levels.GetProperty("entry").GetString(), levels.GetProperty("stop").GetString(), levels.GetProperty("target").GetString()).ShouldBe(("186.50", "178.20", "204.00"));
        await using var scope = factory.Services.CreateAsyncScope();
        var source = scope.ServiceProvider.GetServices<IPrioritySymbolSource>().Single(s => s.Priority == 3);
        (await source.GetSymbolsAsync(default)).ShouldContain("NVDA");
    }

    [Fact]
    public async Task The_schedule_skips_a_run_when_market_data_has_not_moved()
    {
        await RunAsync();
        var before = await QueryAsync(db => db.ScannerRuns.CountAsync());

        await RunAsync("schedule");

        (await QueryAsync(db => db.ScannerRuns.CountAsync())).ShouldBe(before);
    }
}

/// <summary>With no market data at all the run fails with SCN-503 and leaves no signals (SYSTEM_DESIGN.md §7.4).</summary>
public class ScannerFailureTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task A_run_without_usable_data_fails_with_SCN_503()
    {
        _ = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();

        var run = await scope.ServiceProvider.GetRequiredService<ScannerService>().RunAsync("manual", default);

        run.Status.ShouldBe(ScannerRunStatus.Failed);
        run.ErrorCode.ShouldBe("SCN-503");
        (await scope.ServiceProvider.GetRequiredService<DiscoveryDbContext>().Signals.CountAsync()).ShouldBe(0);
    }
}
