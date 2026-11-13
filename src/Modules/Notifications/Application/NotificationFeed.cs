using System.Globalization;
using System.Text;
using Astryx.Modules.Notifications.Domain;
using Astryx.Modules.Notifications.Infrastructure.Persistence;
using Astryx.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Notifications.Application;

public sealed record NotificationDto(Guid Id, string Category, string Type, string Title, string Body, string Severity, string DeepLink, string? EntityType, Guid? EntityId, DateTimeOffset CreatedAt, bool Read);

/// <summary>N1: newest first, with the unread count across all categories (the bell's dot).</summary>
public sealed record NotificationPageDto(IReadOnlyList<NotificationDto> Items, string? NextCursor, int UnreadCount);

public sealed record ReadAllRequest(string? Category);

/// <summary>N1–N3 (SYSTEM_DESIGN.md §11.10). The user-scoped context makes another user's notification a 404.</summary>
public sealed class NotificationFeed(NotificationsDbContext db, TimeProvider timeProvider)
{
    public static readonly Error BadCategory = new("bad_request", "category must be all, orders, alerts, strategy or system.");
    public static readonly Error NotFound = new("not_found", "No notification with that id.");

    public async Task<Result<NotificationPageDto>> ListAsync(string? category, bool? unreadOnly, int? limit, string? cursor, CancellationToken cancellationToken)
    {
        if (category is not (null or "all") && !NotificationCategory.All.Contains(category))
        {
            return BadCategory;
        }

        var query = db.Notifications.AsNoTracking();
        if (category is not (null or "all"))
        {
            query = query.Where(n => n.Category == category);
        }

        if (unreadOnly == true)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        if (cursor is not null && Decode(cursor) is { } after)
        {
            query = query.Where(n => n.CreatedAt < after.At || (n.CreatedAt == after.At && n.Id.CompareTo(after.Id) < 0));
        }

        var take = Math.Clamp(limit ?? 30, 1, 100);
        var rows = await query.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Take(take + 1).ToListAsync(cancellationToken);
        var unread = await db.Notifications.CountAsync(n => n.ReadAt == null, cancellationToken);
        var items = rows.Take(take).Select(n => new NotificationDto(n.Id, n.Category, n.Type, n.Title, n.Body, n.Severity, n.DeepLink, n.EntityType, n.EntityId, n.CreatedAt, n.ReadAt != null)).ToList();
        return new NotificationPageDto(items, rows.Count > take ? Encode(rows[take - 1]) : null, unread);
    }

    public async Task<Result> MarkReadAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await db.Notifications.AnyAsync(n => n.Id == id, cancellationToken))
        {
            return NotFound;
        }

        var now = timeProvider.GetUtcNow();
        await db.Notifications.Where(n => n.Id == id && n.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), cancellationToken);
        return Result.Success();
    }

    public async Task<Result> MarkAllReadAsync(string? category, CancellationToken cancellationToken)
    {
        if (category is not (null or "all") && !NotificationCategory.All.Contains(category))
        {
            return BadCategory;
        }

        var now = timeProvider.GetUtcNow();
        var query = db.Notifications.Where(n => n.ReadAt == null);
        if (category is not (null or "all"))
        {
            query = query.Where(n => n.Category == category);
        }

        await query.ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), cancellationToken);
        return Result.Success();
    }

    private static string Encode(Notification last) => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{last.CreatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}|{last.Id:N}"));

    private static (DateTimeOffset At, Guid Id)? Decode(string cursor)
    {
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
            return parts.Length == 2 && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) && Guid.TryParseExact(parts[1], "N", out var id)
                ? (new DateTimeOffset(ticks, TimeSpan.Zero), id) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
