using Astryx.Infrastructure.Http;
using Astryx.Modules.Notifications.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Astryx.Modules.Notifications.Api;

/// <summary>N1–N3 (SYSTEM_DESIGN.md §11.10). N6 is <c>GET /system/status</c>.</summary>
public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/notifications").WithTags("Notifications").RequireAuthorization("Authenticated");

        group.MapGet("/", async (string? category, bool? unreadOnly, int? limit, string? cursor, NotificationFeed feed, CancellationToken ct) =>
                (await feed.ListAsync(category, unreadOnly, limit, cursor, ct)).Match(Results.Ok, e => e.ToProblem()))
            .WithName("ListNotifications").Produces<NotificationPageDto>().ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{id:guid}/read", async (Guid id, NotificationFeed feed, CancellationToken ct) =>
                await feed.MarkReadAsync(id, ct) is { IsSuccess: false } r ? r.Error.ToProblem() : Results.NoContent())
            .WithName("MarkNotificationRead").Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/read-all", async (ReadAllRequest? request, NotificationFeed feed, CancellationToken ct) =>
                await feed.MarkAllReadAsync(request?.Category, ct) is { IsSuccess: false } r ? r.Error.ToProblem() : Results.NoContent())
            .WithName("MarkAllNotificationsRead").Produces(StatusCodes.Status204NoContent);

        return app;
    }
}
