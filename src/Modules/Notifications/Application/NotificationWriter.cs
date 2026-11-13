using System.Globalization;
using Astryx.Contracts.Events;
using Astryx.Contracts.Events.Ai;
using Astryx.Contracts.Events.Paper;
using Astryx.Contracts.Events.Strategies;
using Astryx.Contracts.Identity;
using Astryx.Infrastructure.Realtime;
using Astryx.Modules.Notifications.Domain;
using Astryx.Modules.Notifications.Infrastructure.Persistence;
using Astryx.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Notifications.Application;

/// <summary><c>activity.created</c> payload (§9.6): the new feed item.</summary>
public sealed record ActivityCreatedMessage(Guid Id, string Category, string Type, string Title, string Body, string Severity, string DeepLink, DateTimeOffset CreatedAt);

/// <summary>
/// Turns outbox events into Activity items (SYSTEM_DESIGN.md §7.15). Each event maps to one notification with a
/// deterministic <c>dedupe_key</c>, inserted with <c>ON CONFLICT DO NOTHING</c>, so at-least-once delivery shows each
/// thing once (IDEM-06). New items are pushed as <c>activity.created</c>.
/// </summary>
public sealed class NotificationWriter(NotificationsDbContext db, IRealtimeNotifier notifier, IUserAccountQuery accounts, TimeProvider timeProvider) :
    IIntegrationEventHandler<OrderFilled>,
    IIntegrationEventHandler<PositionClosed>,
    IIntegrationEventHandler<OrderExpired>,
    IIntegrationEventHandler<StopProximityReached>,
    IIntegrationEventHandler<AnalysisUpdated>,
    IIntegrationEventHandler<StrategyGuardChanged>
{
    public const string ActivityCreated = "activity.created";

    public Task HandleAsync(OrderFilled e, IntegrationEventContext context, CancellationToken cancellationToken) =>
        e.Purpose != "entry"
            ? Task.CompletedTask // exits are announced by PositionClosed, with the P&L
            : WriteAsync(e.UserId, NotificationCategory.Orders, "order_filled", $"Filled (simulated) · {e.Symbol}",
                $"Bought {Shares(e.Quantity)} {e.Symbol} at ${Money(e.Price)} in your paper account.", "success", $"/position/{e.Symbol}", "order", e.OrderId, $"order_filled:{e.OrderId}", cancellationToken);

    public Task HandleAsync(PositionClosed e, IntegrationEventContext context, CancellationToken cancellationToken)
    {
        var (type, title, severity) = e.ExitReason switch
        {
            "stop" => ("stop_hit", $"Stop hit · {e.Symbol}", "warning"),
            "target" => ("target_hit", $"Target hit · {e.Symbol}", "success"),
            _ => ("position_closed", $"Position closed · {e.Symbol}", "info"),
        };
        var pnl = (e.RealizedPnl < 0 ? "−$" : "+$") + Money(Math.Abs(e.RealizedPnl));
        var body = $"Sold at ${Money(e.ExitPrice)} · realized {pnl}.{(e.GappedThroughStop ? " Stop filled below plan (gap)." : string.Empty)}";
        return WriteAsync(e.UserId, NotificationCategory.Orders, type, title, body, severity, $"/position/{e.Symbol}", "position", e.PositionId, $"position_closed:{e.PositionId}", cancellationToken);
    }

    public Task HandleAsync(OrderExpired e, IntegrationEventContext context, CancellationToken cancellationToken) =>
        WriteAsync(e.UserId, NotificationCategory.Orders, "order_expired", $"Order expired · {e.Symbol}",
            "The buy stop wasn't triggered before the close. Its reserved cash is released.", "info", "/activity", "order", e.OrderId, $"order_expired:{e.OrderId}", cancellationToken);

    public Task HandleAsync(StopProximityReached e, IntegrationEventContext context, CancellationToken cancellationToken)
    {
        var pct = (e.Last - e.Stop) / e.Last * 100;
        return WriteAsync(e.UserId, NotificationCategory.Alerts, "stop_proximity", $"{e.Symbol} near its stop",
            $"{e.Symbol} is {pct.ToString("0.0", CultureInfo.InvariantCulture)}% above its stop at ${Money(e.Stop)}.", "warning", $"/position/{e.Symbol}", "position", e.PositionId,
            $"stop_proximity:{e.PositionId}:{DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime):yyyy-MM-dd}", cancellationToken);
    }

    public Task HandleAsync(AnalysisUpdated e, IntegrationEventContext context, CancellationToken cancellationToken) => e.Status switch
    {
        "completed" => WriteAsync(e.UserId, NotificationCategory.Alerts, "analysis_ready", $"Analysis ready · {e.Symbol}",
            "The AI analysis finished. Experimental model: research only.", "info", $"/stock/{e.Symbol}?tab=signal", "analysis_job", e.JobId, $"analysis:{e.JobId}", cancellationToken),
        "failed" => WriteAsync(e.UserId, NotificationCategory.System, "analysis_failed", $"Analysis failed · {e.Symbol}",
            "Analysis couldn't be generated reliably. Indicators are still available.", "error", $"/stock/{e.Symbol}?tab=signal", "analysis_job", e.JobId, $"analysis:{e.JobId}", cancellationToken),
        _ => Task.CompletedTask,
    };

    /// <summary>Strategies are shared, so a guard change is fanned out to every account (design 17 "drawdown guard").</summary>
    public async Task HandleAsync(StrategyGuardChanged e, IntegrationEventContext context, CancellationToken cancellationToken)
    {
        var drawdown = Pct(e.Drawdown);
        var pause = Pct(e.PausePct);
        var (type, title, body, severity) = e.Status switch
        {
            "paused" => ("strategy_paused", $"{e.Name} paused", $"{e.WindowDays}-day drawdown {drawdown} reached the {pause} guard. New signals from this strategy are paused.", "error"),
            "active_guarded" => ("strategy_guard", $"{e.Name} drawdown guard", $"{e.WindowDays}-day drawdown {drawdown} · pauses at {pause}", "warning"),
            _ => ("strategy_guard_cleared", $"{e.Name} guard cleared", $"{e.WindowDays}-day drawdown recovered to {drawdown}. Signals continue as normal.", "info"),
        };
        var dedupeKey = $"strategy_guard:{e.StrategyId}:{e.Status}:{e.AsOf:yyyy-MM-dd}";
        foreach (var userId in await accounts.ListActiveIdsAsync(cancellationToken))
        {
            await WriteAsync(userId, NotificationCategory.Strategy, type, title, body, severity, $"/strategy/{e.Code}", "strategy", e.StrategyId, dedupeKey, cancellationToken);
        }
    }

    private async Task WriteAsync(
        Guid userId, string category, string type, string title, string body, string severity, string deepLink, string entityType, Guid entityId, string dedupeKey, CancellationToken cancellationToken)
    {
        var id = Ids.New(timeProvider);
        var now = timeProvider.GetUtcNow();
        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO notify.notifications (id, user_id, category, type, title, body, severity, deep_link, entity_type, entity_id, dedupe_key, created_at)
            VALUES ({id}, {userId}, {category}, {type}, {title}, {body}, {severity}, {deepLink}, {entityType}, {entityId}, {dedupeKey}, {now})
            ON CONFLICT (user_id, dedupe_key) DO NOTHING
            """,
            cancellationToken);
        if (inserted == 1)
        {
            await notifier.SendToUserAsync(userId, ActivityCreated, new ActivityCreatedMessage(id, category, type, title, body, severity, deepLink, now), cancellationToken);
        }
    }

    /// <summary>−0.054 → "−5.4%".</summary>
    private static string Pct(decimal fraction) => (fraction < 0 ? "−" : string.Empty) + Math.Round(Math.Abs(fraction) * 100, 1, MidpointRounding.ToEven).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private static string Money(decimal value) => Math.Round(value, 2, MidpointRounding.ToEven).ToString("N2", CultureInfo.InvariantCulture);

    private static string Shares(decimal value) => Math.Round(value, 0).ToString(CultureInfo.InvariantCulture);
}
