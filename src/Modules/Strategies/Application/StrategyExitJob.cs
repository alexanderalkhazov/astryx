using System.Text.Json.Nodes;
using Astryx.Contracts.MarketData;
using Astryx.Contracts.Paper;
using Astryx.Contracts.Portfolio;
using Astryx.Contracts.Quant;
using Astryx.Infrastructure.Jobs;
using Astryx.Modules.Strategies.Application.Rules;
using Astryx.Modules.Strategies.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Astryx.Modules.Strategies.Application;

public sealed record StrategyExitJob : IJob
{
    public static string JobType => "strategies.exit.v1";
}

/// <summary>
/// Time and indicator exits (TASKS.md T-163) for rules a bracket can't express (RSI(2): RSI back above its exit level or
/// held too long; Earnings Drift: held its full period). From five minutes after the open, each open strategy position is
/// judged on its last <b>completed</b> bar and sold through the user's own close path, the same decision the backtester
/// makes at the close and executes at the next open. The idempotency key is per position, so repeated runs close once.
/// </summary>
public sealed class StrategyExitHandler(
    StrategiesDbContext db,
    IEnumerable<IStrategyRule> rules,
    IStrategyPositions positions,
    IPriceHistoryProvider history,
    IPositionCloser closer,
    IMarketSessionClock sessions,
    ILogger<StrategyExitHandler> logger) : IJobHandler<StrategyExitJob>
{
    private static readonly TimeSpan AfterOpen = TimeSpan.FromMinutes(5L);
    private static readonly TimeSpan BeforeClose = TimeSpan.FromMinutes(5L);

    public async Task HandleAsync(StrategyExitJob job, JobContext context, CancellationToken cancellationToken)
    {
        var now = sessions.Now;
        if (await sessions.TodayAsync(cancellationToken) is not { } session || now < session.OpenAt + AfterOpen || now >= session.CloseAt - BeforeClose)
        {
            return;
        }

        var exitRules = rules.OfType<IStrategyExit>().Cast<IStrategyRule>().ToDictionary(r => r.Code);
        if (exitRules.Count == 0)
        {
            return;
        }

        var open = await positions.ListOpenAsync(cancellationToken);
        var versionIds = open.Select(p => p.StrategyVersionId).Distinct().ToList();
        var versions = await db.Versions.AsNoTracking()
            .Where(v => versionIds.Contains(v.Id))
            .Join(db.Strategies.AsNoTracking(), v => v.StrategyId, s => s.Id, (v, s) => new { v.Id, s.Code, v.Params })
            .ToDictionaryAsync(v => v.Id, cancellationToken);

        foreach (var position in open)
        {
            if (!versions.TryGetValue(position.StrategyVersionId, out var version) || !exitRules.TryGetValue(version.Code, out var rule)
                || await history.GetDailyAsync(position.Symbol, Backtesting.Backtester.ExitWindow + 1, cancellationToken) is not { } daily)
            {
                continue;
            }

            var completed = daily.Bars.Where(b => b.Final).ToList(); // today's forming bar is not a decision bar
            var opened = DateOnly.FromDateTime(position.OpenedAt.UtcDateTime);
            var held = completed.Count(b => b.SessionDate > opened);
            var reason = ((IStrategyExit)rule).ExitReason(new QuantFactsView(completed.Select(b => b.Close).ToList()), held, JsonNode.Parse(version.Params)!.AsObject());
            if (reason is null)
            {
                continue;
            }

            if (await closer.CloseAsync(position.UserId, position.PositionId, $"strategy-exit:{position.PositionId}", "strategy", cancellationToken))
            {
                logger.LogInformation("Strategy exit ({Reason}) closed position {PositionId} ({Symbol}) after {Held} sessions", reason, position.PositionId, position.Symbol, held);
            }
        }
    }
}
