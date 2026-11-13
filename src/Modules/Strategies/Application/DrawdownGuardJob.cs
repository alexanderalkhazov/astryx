using Astryx.Contracts.Events.Strategies;
using Astryx.Infrastructure.Jobs;
using Astryx.Modules.Strategies.Domain;
using Astryx.Modules.Strategies.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Astryx.Modules.Strategies.Application;

/// <summary>
/// <c>DrawdownGuardJob</c> (SYSTEM_DESIGN.md §7.8): hourly. Evaluates every system strategy's live paper track, stores
/// the drawdown and status, and publishes <see cref="StrategyGuardChanged"/> once per status that has not been
/// announced yet. Re-running with no new points changes nothing.
/// </summary>
public sealed record DrawdownGuardJob : IJob
{
    public static string JobType => "strategies.drawdown_guard.v1";
}

public sealed class DrawdownGuardHandler(StrategiesDbContext db, TimeProvider timeProvider, ILogger<DrawdownGuardHandler> logger) : IJobHandler<DrawdownGuardJob>
{
    public async Task HandleAsync(DrawdownGuardJob job, JobContext context, CancellationToken cancellationToken)
    {
        var strategies = await db.Strategies.Where(s => s.OwnerUserId == null && s.Status != StrategyStatus.Retired).ToListAsync(cancellationToken);
        foreach (var strategy in strategies)
        {
            // Twice the window is enough to find the latest point and its window, whatever the gaps.
            var latest = await db.LivePoints.Where(p => p.StrategyId == strategy.Id).MaxAsync(p => (DateOnly?)p.D, cancellationToken);
            if (latest is null)
            {
                continue;
            }

            var from = latest.Value.AddDays(-strategy.GuardWindowDays);
            var points = await db.LivePoints.AsNoTracking().Where(p => p.StrategyId == strategy.Id && p.D > from)
                .Select(p => new { p.D, p.Equity }).ToListAsync(cancellationToken);
            var evaluation = DrawdownGuard.Evaluate(points.Select(p => (p.D, p.Equity)).ToList(), strategy.Status, strategy.GuardPausePct, strategy.GuardWindowDays)!;

            var previous = strategy.GuardAnnouncedStatus ?? StrategyStatus.Active;
            strategy.GuardDrawdown = evaluation.Drawdown;
            strategy.GuardAsOf = evaluation.AsOf;
            if (strategy.Status != evaluation.Status)
            {
                strategy.Status = evaluation.Status;
                strategy.UpdatedAt = timeProvider.GetUtcNow();
            }

            if (evaluation.Status != previous)
            {
                db.Publish(new StrategyGuardChanged(
                    strategy.Id, strategy.Code, strategy.Name, previous, evaluation.Status, evaluation.Drawdown, strategy.GuardPausePct, strategy.GuardWindowDays, evaluation.AsOf));
                logger.LogInformation("Strategy {Code} drawdown guard: {Previous} → {Status} at {Drawdown:P1}", strategy.Code, previous, evaluation.Status, evaluation.Drawdown);
            }

            strategy.GuardAnnouncedStatus = evaluation.Status;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
