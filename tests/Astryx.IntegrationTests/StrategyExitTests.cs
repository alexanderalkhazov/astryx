using Astryx.Contracts.MarketData;
using Astryx.Contracts.Paper;
using Astryx.Contracts.Portfolio;
using Astryx.Modules.Strategies.Application;
using Astryx.Modules.Strategies.Application.Rules;
using Astryx.Modules.Strategies.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Astryx.IntegrationTests;

/// <summary>The strategy exit job (TASKS.md T-163) against the seeded catalog, with the market and broker stubbed.</summary>
public class StrategyExitTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-24T16:00:00Z", System.Globalization.CultureInfo.InvariantCulture); // 11:00 ET

    private sealed class Positions(params StrategyPosition[] open) : IStrategyPositions
    {
        public Task<IReadOnlyList<StrategyPosition>> ListOpenAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<StrategyPosition>>(open);
    }

    private sealed class History(IReadOnlyList<decimal> closes) : IPriceHistoryProvider
    {
        public Task<DailyHistory?> GetDailyAsync(string symbol, int maxBars, CancellationToken cancellationToken, AdjustmentMode mode = AdjustmentMode.SplitOnly)
        {
            var start = new DateOnly(2026, 6, 1);
            var bars = closes.Select((c, i) => new DailyBar(start.AddDays(i), c, c, c, c, 1_000_000, Final: true))
                .Append(new DailyBar(DateOnly.FromDateTime(Now.UtcDateTime), 1m, 1m, 1m, 1m, 1, Final: false)) // today's forming bar: ignored
                .ToList();
            return Task.FromResult<DailyHistory?>(new DailyHistory(Guid.NewGuid(), symbol, bars, null, false, []));
        }

        public Task<CloseLadder> GetCloseLadderAsync(IReadOnlyList<int> sessionsBack, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, PriceTrail>> GetTrailsAsync(IReadOnlyCollection<string> symbols, int sessions, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Closer : IPositionCloser
    {
        public List<(Guid User, Guid Position, string Key, string Reason)> Closed { get; } = [];

        public Task<bool> CloseAsync(Guid userId, Guid positionId, string idempotencyKey, string exitReason, CancellationToken cancellationToken)
        {
            Closed.Add((userId, positionId, idempotencyKey, exitReason));
            return Task.FromResult(true);
        }
    }

    private sealed class Clock(bool open) : IMarketSessionClock
    {
        public DateTimeOffset Now => StrategyExitTests.Now;

        public Task<TradingSession?> TodayAsync(CancellationToken cancellationToken) => Task.FromResult<TradingSession?>(open
            ? new TradingSession(new DateOnly(2026, 11, 24), Now.AddHours(-1.5), Now.AddHours(5), false)
            : null);
    }

    private async Task<Guid> VersionAsync(string code)
    {
        _ = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StrategiesDbContext>();
        return await db.Strategies.Where(s => s.Code == code).Select(s => s.PublishedVersionId!.Value).SingleAsync();
    }

    private async Task<Closer> RunAsync(IStrategyPositions positions, IReadOnlyList<decimal> closes, bool open = true)
    {
        var closer = new Closer();
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = new StrategyExitHandler(
            scope.ServiceProvider.GetRequiredService<StrategiesDbContext>(), scope.ServiceProvider.GetServices<IStrategyRule>(), positions, new History(closes), closer, new Clock(open), NullLogger<StrategyExitHandler>.Instance);
        await handler.HandleAsync(new StrategyExitJob(), new Astryx.Infrastructure.Jobs.JobContext(Guid.NewGuid(), 1, 1, null), default);
        return closer;
    }

    [Fact]
    public async Task An_RSI2_position_is_sold_once_RSI2_rebounds_and_a_breakout_position_is_left_to_its_bracket()
    {
        var rsi2 = new StrategyPosition(Guid.NewGuid(), Guid.NewGuid(), "AAA", await VersionAsync("rsi2_mean_reversion"), Now.AddDays(-3));
        var breakout = new StrategyPosition(Guid.NewGuid(), Guid.NewGuid(), "BBB", await VersionAsync("base_breakout"), Now.AddDays(-3));
        var rebound = Enumerable.Range(0, 60).Select(i => 100m + i * 0.1m).Concat([104m, 101m, 99m, 103m, 107m]).ToList(); // RSI(2) back above 70

        var closer = await RunAsync(new Positions(rsi2, breakout), rebound);

        closer.Closed.ShouldHaveSingleItem().ShouldBe((rsi2.UserId, rsi2.PositionId, $"strategy-exit:{rsi2.PositionId}", "strategy"));
    }

    [Fact]
    public async Task An_earnings_drift_position_is_held_until_its_holding_period_ends()
    {
        var version = await VersionAsync("earnings_drift");
        var flat = Enumerable.Repeat(100m, 65).ToList(); // bars end yesterday: sessions after the open date count as held
        var recent = new StrategyPosition(Guid.NewGuid(), Guid.NewGuid(), "CCC", version, new DateTimeOffset(2026, 7, 26, 15, 0, 0, TimeSpan.Zero)); // 10 sessions held
        var old = new StrategyPosition(Guid.NewGuid(), Guid.NewGuid(), "DDD", version, new DateTimeOffset(2026, 6, 20, 15, 0, 0, TimeSpan.Zero)); // well past 20

        var closer = await RunAsync(new Positions(recent, old), flat);

        closer.Closed.Select(c => c.Position).ShouldBe([old.PositionId]);
    }

    [Fact]
    public async Task Nothing_is_sold_outside_the_session()
    {
        var rsi2 = new StrategyPosition(Guid.NewGuid(), Guid.NewGuid(), "AAA", await VersionAsync("rsi2_mean_reversion"), Now.AddDays(-30));

        (await RunAsync(new Positions(rsi2), Enumerable.Repeat(100m, 60).ToList(), open: false)).Closed.ShouldBeEmpty();
    }
}
