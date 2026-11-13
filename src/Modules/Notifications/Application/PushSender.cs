using Astryx.Contracts.Profiles;
using Astryx.Infrastructure.Jobs;
using Astryx.Modules.Notifications.Domain;
using Astryx.Modules.Notifications.Infrastructure.Persistence;
using Astryx.Modules.Notifications.Infrastructure.Push;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Astryx.Modules.Notifications.Application;

/// <summary>Pushes one Activity item to the user's devices (SYSTEM_DESIGN.md §7.15, §19.2 <c>PushSender</c>).</summary>
public sealed record PushNotificationJob(Guid NotificationId) : IJob
{
    public static string JobType => "notifications.push.v1";
}

/// <summary>
/// Sends at most once per notification (<c>pushed_at</c> is claimed before sending), only when the user enabled push
/// and the item's preference category (S4), to every live device. Expo's <c>DeviceNotRegistered</c> revokes the token.
/// The payload carries the item's deep link, which the app opens on tap.
/// </summary>
public sealed class PushSender(NotificationsDbContext db, INotificationPreferencesSource preferences, IPushGateway gateway, TimeProvider timeProvider, ILogger<PushSender> logger)
    : IJobHandler<PushNotificationJob>
{
    /// <summary>The S4 preference that governs an item type. Unknown types count as System.</summary>
    public static bool Wants(PushPreferences p, string type) => type switch
    {
        "order_filled" or "stop_hit" or "target_hit" or "position_closed" or "order_expired" => p.Fills,
        "stop_proximity" => p.StopProximity,
        "analysis_ready" or "signal_new" => p.Signals,
        "strategy_guard" or "strategy_paused" or "strategy_guard_cleared" => p.StrategyGuard,
        "daily_summary" => p.DailySummary,
        _ => p.System,
    };

    public async Task HandleAsync(PushNotificationJob job, JobContext context, CancellationToken cancellationToken)
    {
        var notification = await db.Notifications.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(n => n.Id == job.NotificationId, cancellationToken);
        if (notification is null || notification.PushedAt is not null)
        {
            return;
        }

        var prefs = await preferences.GetAsync(notification.UserId, cancellationToken);
        var devices = await db.PushDevices.IgnoreQueryFilters().AsNoTracking().Where(d => d.UserId == notification.UserId && d.RevokedAt == null).ToListAsync(cancellationToken);
        if (!prefs.PushEnabled || !Wants(prefs, notification.Type) || devices.Count == 0)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var claimed = await db.Notifications.IgnoreQueryFilters().Where(n => n.Id == notification.Id && n.PushedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.PushedAt, now), cancellationToken);
        if (claimed == 0)
        {
            return; // a concurrent attempt already sent it
        }

        var data = new Dictionary<string, string> { ["deepLink"] = notification.DeepLink, ["notificationId"] = notification.Id.ToString() };
        IReadOnlyList<PushOutcome> outcomes;
        try
        {
            outcomes = await gateway.SendAsync(devices.Select(d => new PushMessage(d.PushToken, notification.Title, notification.Body, data)).ToList(), cancellationToken);
        }
        catch
        {
            // Release the claim so the retry can send.
            await db.Notifications.IgnoreQueryFilters().Where(n => n.Id == notification.Id).ExecuteUpdateAsync(s => s.SetProperty(n => n.PushedAt, (DateTimeOffset?)null), CancellationToken.None);
            throw;
        }

        var dead = outcomes.Where(o => o.Error == "DeviceNotRegistered").Select(o => o.To).ToList();
        if (dead.Count > 0)
        {
            await db.PushDevices.IgnoreQueryFilters().Where(d => dead.Contains(d.PushToken) && d.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.RevokedAt, now).SetProperty(d => d.RevokedReason, "device_not_registered"), cancellationToken);
            logger.LogInformation("Revoked {Count} push token(s) Expo reported as not registered", dead.Count);
        }
    }
}
