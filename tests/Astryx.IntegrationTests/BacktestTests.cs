using System.Net.Http.Json;
using System.Text.Json;
using Astryx.Modules.Strategies.Application.Backtesting;
using Astryx.Modules.Strategies.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.IntegrationTests;

/// <summary>A real backtest end to end on the synced fake market (TASKS.md T-160).</summary>
public class BacktestTests(TradingApiFactory factory) : IClassFixture<TradingApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.PrepareAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_backtest_replaces_the_illustrative_figures_with_a_recorded_run_and_its_benchmark()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<BacktestService>();

        var summary = await service.RunAsync("base_breakout", default);

        summary.ShouldNotBeNull();
        summary.PeriodEnd.ShouldBeGreaterThan(summary.PeriodStart); // after the year the relative-strength rank needs
        summary.EqualWeightMultiple.ShouldNotBeNull().ShouldBeGreaterThan(0m);
        var db = scope.ServiceProvider.GetRequiredService<StrategiesDbContext>();
        var run = await db.Runs.AsNoTracking().SingleAsync(r => r.Id == summary.RunId);
        using (var assumptions = JsonDocument.Parse(run.Assumptions))
        {
            assumptions.RootElement.GetProperty("illustrative").GetBoolean().ShouldBeFalse();
            assumptions.RootElement.GetProperty("benchmark").GetProperty("name").GetString().ShouldBe("holding every stock equally");
        }

        (await db.Trades.CountAsync(t => t.RunId == run.Id)).ShouldBe(summary.Trades);

        var client = await TestUsers.SignedInClientAsync(factory);
        var strategies = await client.GetFromJsonAsync<JsonElement>("/api/v1/strategies");
        var breakout = strategies.GetProperty("items").EnumerateArray().Single(s => s.GetProperty("code").GetString() == "base_breakout");
        breakout.GetProperty("backtestIllustrative").GetBoolean().ShouldBeFalse();
        breakout.GetProperty("backtestEnd").GetString().ShouldBe(summary.PeriodEnd.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        strategies.GetProperty("items").EnumerateArray().Single(s => s.GetProperty("code").GetString() == "trend_momentum")
            .GetProperty("backtestIllustrative").GetBoolean().ShouldBeTrue(); // still the seeded figures
    }

    [Fact]
    public async Task An_unknown_strategy_has_nothing_to_backtest()
    {
        await using var scope = factory.Services.CreateAsyncScope();

        (await scope.ServiceProvider.GetRequiredService<BacktestService>().RunAsync("user_custom_strategy", default)).ShouldBeNull();
    }
}
