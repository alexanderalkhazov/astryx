using Astryx.Infrastructure.Security;

namespace Astryx.Modules.Notifications.Domain;

public static class NotificationCategory
{
    public const string Orders = "orders";
    public const string Alerts = "alerts";
    public const string Strategy = "strategy";
    public const string System = "system";

    public static readonly string[] All = [Orders, Alerts, Strategy, System];
}

/// <summary>Row in <c>notify.notifications</c>, the Activity feed (SYSTEM_DESIGN.md §7.15, §10.4).</summary>
public sealed class Notification : IUserOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public required string Category { get; set; }

    /// <summary>e.g. <c>order_filled</c>, <c>stop_hit</c>, <c>stop_proximity</c>, <c>analysis_ready</c>.</summary>
    public required string Type { get; set; }

    public required string Title { get; set; }

    public required string Body { get; set; }

    /// <summary><c>info</c>, <c>success</c>, <c>warning</c> or <c>error</c>.</summary>
    public required string Severity { get; set; }

    /// <summary>Expo Router path, e.g. <c>/position/NVDA</c>.</summary>
    public required string DeepLink { get; set; }

    public string? EntityType { get; set; }

    public Guid? EntityId { get; set; }

    /// <summary>Unique per user: a re-delivered event never creates a second notification (IDEM-06).</summary>
    public required string DedupeKey { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ReadAt { get; set; }

    public DateTimeOffset? PushedAt { get; set; }
}

public static class PushPlatform
{
    public const string Ios = "ios";
    public const string Android = "android";
}

/// <summary>Row in <c>notify.push_devices</c> (§10.4): one Expo push token per device. Revoked rows are kept for audit.</summary>
public sealed class PushDevice : IUserOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>The app's install id (the one sent at sign-in).</summary>
    public required string DeviceId { get; set; }

    /// <summary><c>ExponentPushToken[…]</c>; unique across users.</summary>
    public required string PushToken { get; set; }

    public required string Platform { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>Set by N5, by a newer registration of the token elsewhere, or when Expo reports DeviceNotRegistered.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    public string? RevokedReason { get; set; }
}
