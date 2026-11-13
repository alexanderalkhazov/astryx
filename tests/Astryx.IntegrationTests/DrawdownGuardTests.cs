using System.Net.Http.Json;
using System.Text.Json;
using Astryx.Infrastructure.Jobs;
using Astryx.Infrastructure.Outbox;
using Astryx.Modules.Strategies.Application;
using Astryx.Modules.Strategies.Infrastructure.Persistence;
using Astryx.Modules.Strategies.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.IntegrationTests;

/// <summary>The strategy drawdown guard job, its status changes and the Activity "Strategy" items (SYSTEM_DESIGN.md §7.8, T-097).</summary>
public abstract class DrawdownGuardScenario(ApiFactory factory)
{
    protected ApiFactory Factory => factory;

    protected async Task RunGuardAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DrawdownGuardHandler>().HandleAsync(new DrawdownGuardJob(), new JobContext(Guid.NewGuid(), 1, 1, null), default);
        var dispatcher = factory.Services.GetRequiredService<OutboxDispatcher>();
        while (await dispatcher.DispatchOnceAsync(default) > 0)
        {
        }
    }

    /// <summary>
    /// The hosted recurring job may already have announced the seeded state when the host started, before the test's
    /// users existed. Forget announcements and strategy items so the next run announces to everyone; concurrent runs
    /// are harmless (one item per strategy, status and day).
    /// </summary>
    protected async Task ResetAnnouncementsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StrategiesDbContext>();
        await db.Database.ExecuteSqlAsync($"UPDATE strategy.strategies SET guard_announced_status = NULL");
        await db.Database.ExecuteSqlAsync($"DELETE FROM notify.notifications WHERE category = 'strategy'");
    }

    protected async Task AddPointAsync(string code, DateOnly d, decimal equity)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StrategiesDbContext>();
        await db.Database.ExecuteSqlAsync($"INSERT INTO strategy.live_points (strategy_id, d, equity) VALUES ({CatalogSeed.StrategyId(code)}, {d}, {equity})");
    }

    protected static async Task<List<JsonElement>> StrategyItemsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications?category=strategy")).GetProperty("items").EnumerateArray().ToList();

    protected static async Task<JsonElement> SummaryAsync(HttpClient client, string code) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/strategies")).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("code").GetString() == code);

    protected async Task<(DateOnly D, decimal Equity)> LastPointAsync(string code)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StrategiesDbContext>();
        var id = CatalogSeed.StrategyId(code);
        var p = await db.LivePoints.Where(x => x.StrategyId == id).OrderByDescending(x => x.D).FirstAsync();
        return (p.D, p.Equity);
    }

    protected async Task<decimal> HighAsync(string code)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StrategiesDbContext>();
        var id = CatalogSeed.StrategyId(code);
        return await db.LivePoints.Where(x => x.StrategyId == id).MaxAsync(x => x.Equity);
    }
}

public class DrawdownGuardTests(ApiFactory factory) : DrawdownGuardScenario(factory), IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Warns_at_the_seeded_minus_5_4_pauses_at_minus_6_and_announces_each_change_once()
    {
        var client = await TestUsers.SignedInClientAsync(Factory);
        var other = await TestUsers.SignedInClientAsync(Factory);
        await ResetAnnouncementsAsync();

        // Seeded track: Trend Pullback sits 5.4 % below its 30-day high (design 17), the others are well clear.
        await RunGuardAsync();
        var pullback = await SummaryAsync(client, "trend_pullback");
        pullback.GetProperty("status").GetString().ShouldBe("active_guarded");
        var guard = pullback.GetProperty("guard");
        (guard.GetProperty("drawdown").GetString(), guard.GetProperty("warnAt").GetString(), guard.GetProperty("pauseAt").GetString()).ShouldBe(("-0.0540", "-0.0540", "-0.0600"));
        guard.GetProperty("windowDays").GetInt32().ShouldBe(30);
        guard.GetProperty("asOf").GetString().ShouldBe("2026-11-09");
        (await SummaryAsync(client, "base_breakout")).GetProperty("status").GetString().ShouldBe("active");

        var items = await StrategyItemsAsync(client);
        var warning = items.ShouldHaveSingleItem();
        warning.GetProperty("title").GetString().ShouldBe("Trend Pullback drawdown guard");
        warning.GetProperty("body").GetString().ShouldBe("30-day drawdown −5.4% · pauses at −6.0%");
        warning.GetProperty("severity").GetString().ShouldBe("warning");
        warning.GetProperty("deepLink").GetString().ShouldBe("/strategy/trend_pullback");
        (await StrategyItemsAsync(other)).Count.ShouldBe(1); // strategies are shared: everyone is told

        // Re-running with no new point announces nothing new.
        await RunGuardAsync();
        (await StrategyItemsAsync(client)).Count.ShouldBe(1);

        // A worse close takes it through −6.0 %: paused (the scanner only evaluates active and guarded strategies).
        var track = await LastPointAsync("trend_pullback");
        await AddPointAsync("trend_pullback", track.D.AddDays(1), Math.Round(track.Equity * 0.99m, 2));
        await RunGuardAsync();
        (await SummaryAsync(client, "trend_pullback")).GetProperty("status").GetString().ShouldBe("paused");
        items = await StrategyItemsAsync(client);
        items.Count.ShouldBe(2);
        items[0].GetProperty("title").GetString().ShouldBe("Trend Pullback paused");
        items[0].GetProperty("body").GetString()!.ShouldStartWith("30-day drawdown −6.");
        items[0].GetProperty("severity").GetString().ShouldBe("error");

        // A pause holds even when the track recovers.
        await AddPointAsync("trend_pullback", track.D.AddDays(2), track.Equity * 1.10m);
        await RunGuardAsync();
        (await SummaryAsync(client, "trend_pullback")).GetProperty("status").GetString().ShouldBe("paused");
        (await StrategyItemsAsync(client)).Count.ShouldBe(2);
    }
}

/// <summary>Its own database: the seeded announcement must not have happened yet.</summary>
public class DrawdownGuardRecoveryTests(ApiFactory factory) : DrawdownGuardScenario(factory), IClassFixture<ApiFactory>
{
    [Fact]
    public async Task A_guarded_strategy_that_recovers_returns_to_active_with_an_info_item()
    {
        var client = await TestUsers.SignedInClientAsync(Factory);
        await ResetAnnouncementsAsync();
        await RunGuardAsync();
        var last = await LastPointAsync("trend_momentum");

        // Warn (−5.5 % from the high), then recover to a new high.
        var high = await HighAsync("trend_momentum");
        await AddPointAsync("trend_momentum", last.D.AddDays(1), Math.Round(high * 0.945m, 2));
        await RunGuardAsync();
        (await SummaryAsync(client, "trend_momentum")).GetProperty("status").GetString().ShouldBe("active_guarded");
        await AddPointAsync("trend_momentum", last.D.AddDays(2), high + 1m);
        await RunGuardAsync();

        (await SummaryAsync(client, "trend_momentum")).GetProperty("status").GetString().ShouldBe("active");
        var items = (await StrategyItemsAsync(client)).Where(i => i.GetProperty("deepLink").GetString() == "/strategy/trend_momentum").ToList();
        items.Select(i => i.GetProperty("title").GetString()).ShouldBe(["Trend Momentum guard cleared", "Trend Momentum drawdown guard"]);
        items[0].GetProperty("severity").GetString().ShouldBe("info");
    }
}
