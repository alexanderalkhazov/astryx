using Astryx.Infrastructure.Security;
using Astryx.Modules.Notifications.Domain;
using Astryx.Modules.Notifications.Infrastructure.Persistence;
using Astryx.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Notifications.Application;

public sealed record RegisterDeviceRequest(string? DeviceId, string? PushToken, string? Platform);

public sealed record PushDeviceDto(string DeviceId, string Platform, DateTimeOffset LastSeenAt);

/// <summary>N4/N5 (SYSTEM_DESIGN.md §11.10): register a device's Expo push token, or stop pushing to it.</summary>
public sealed class PushDevices(NotificationsDbContext db, ICurrentUser user, TimeProvider timeProvider)
{
    /// <summary>
    /// Upsert by (user, device). A token is unique: if another row holds it (a reinstall, or the phone changed hands),
    /// that row is revoked first, so one token never reaches two accounts.
    /// </summary>
    public async Task<PushDeviceDto> RegisterAsync(string deviceId, string pushToken, string platform, CancellationToken cancellationToken)
    {
        var userId = user.RequireUserId();
        var now = timeProvider.GetUtcNow();
        await db.PushDevices.IgnoreQueryFilters()
            .Where(d => d.PushToken == pushToken && !(d.UserId == userId && d.DeviceId == deviceId))
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.PushToken, d => "revoked:" + d.Id).SetProperty(d => d.RevokedAt, now).SetProperty(d => d.RevokedReason, "token_moved"), cancellationToken);

        var device = await db.PushDevices.SingleOrDefaultAsync(d => d.UserId == userId && d.DeviceId == deviceId, cancellationToken);
        if (device is null)
        {
            device = new PushDevice { Id = Ids.New(timeProvider), UserId = userId, DeviceId = deviceId, PushToken = pushToken, Platform = platform, CreatedAt = now, LastSeenAt = now };
            db.PushDevices.Add(device);
        }
        else
        {
            device.PushToken = pushToken;
            device.Platform = platform;
            device.LastSeenAt = now;
            device.RevokedAt = null;
            device.RevokedReason = null;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new PushDeviceDto(device.DeviceId, device.Platform, device.LastSeenAt);
    }

    /// <summary>Idempotent; another user's device id simply matches nothing.</summary>
    public async Task UnregisterAsync(string deviceId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await db.PushDevices.Where(d => d.DeviceId == deviceId && d.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.RevokedAt, now).SetProperty(d => d.RevokedReason, "unregistered"), cancellationToken);
    }
}
