using Astryx.Modules.Strategies.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.IntegrationTests;

/// <summary>The Base Breakout rule on the NVDA fixture reproduces design 04 (SYSTEM_DESIGN.md §13.4.1, T-064).</summary>
public class BaseBreakoutTests(SyncedMarketApiFactory factory) : IClassFixture<SyncedMarketApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.EnsureSyncedAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Astryx.Modules.Strategies.Application.Rules.RuleResult?> EvaluateAsync(string symbol)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<StrategyEvaluator>().EvaluateAsync("base_breakout", symbol, default);
    }

    [Fact]
    public async Task Nvda_produces_the_design_signal()
    {
        var result = await EvaluateAsync("NVDA");

        result.ShouldNotBeNull();
        result.Miss.ShouldBeNull(result.Miss?.Detail);
        var signal = result.Candidate!;
        (signal.OrderType, signal.Entry, signal.Stop, signal.Target).ShouldBe(("buy_stop", 186.50m, 178.20m, 204.00m));
        signal.RewardRisk.ShouldBe(2.11m);
        signal.Score.ShouldBe(87);
        signal.Reason.ShouldBe("Cleared a 6-week base at $185.90 on 2.4× average volume.");
        signal.SignalType.ShouldBe("breakout");
        signal.Breakdown.Keys.ShouldBe(["setup", "relativeStrength", "volume", "trend", "rewardRisk"]);
        signal.Breakdown["volume"].ShouldBe(16.00m); // 20 × 2.4 / 3
        signal.Breakdown["trend"].ShouldBe(15.00m);
    }

    [Fact]
    public async Task A_stock_without_a_base_has_no_signal_with_a_reason()
    {
        var result = await EvaluateAsync("TSLA"); // the downtrend fixture

        result.ShouldNotBeNull();
        result.Candidate.ShouldBeNull();
        result.Miss!.Code.ShouldBeOneOf("no_breakout", "base_too_deep", "volume_too_low", "weak_relative_strength", "downtrend");
    }

    [Fact]
    public async Task Unknown_strategies_and_symbols_give_no_result()
    {
        (await EvaluateAsync("NOPE")).ShouldBeNull();
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<StrategyEvaluator>().EvaluateAsync("user_custom_strategy", "NVDA", default)).ShouldBeNull(); // no rule with that code
    }
}
