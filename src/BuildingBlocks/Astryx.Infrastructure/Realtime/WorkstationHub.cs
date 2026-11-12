using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Infrastructure.Realtime;

/// <summary>
/// <c>/hubs/workstation</c> (SYSTEM_DESIGN.md §9.6): server-to-client events only, one group per user. The JWT comes
/// in the <c>access_token</c> query string on WebSocket connects. Every event is also reachable by polling REST.
/// </summary>
[Authorize(Policy = "Authenticated")]
public sealed class WorkstationHub : Hub
{
    public const string Path = "/hubs/workstation";

    public static string UserGroup(Guid userId) => $"user:{userId}";

    public override async Task OnConnectedAsync()
    {
        if (Guid.TryParse(Context.User?.FindFirst("sub")?.Value, out var userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
        }

        await base.OnConnectedAsync();
    }
}

/// <summary>Pushes a §9.6 event to every connection of one user.</summary>
public interface IRealtimeNotifier
{
    Task SendToUserAsync(Guid userId, string eventName, object payload, CancellationToken cancellationToken);
}

public sealed class HubRealtimeNotifier(IHubContext<WorkstationHub> hub) : IRealtimeNotifier
{
    public Task SendToUserAsync(Guid userId, string eventName, object payload, CancellationToken cancellationToken) =>
        hub.Clients.Group(WorkstationHub.UserGroup(userId)).SendAsync(eventName, payload, cancellationToken);
}

public static class RealtimeRegistration
{
    public static IServiceCollection AddRealtime(this IServiceCollection services)
    {
        services.AddSignalR(options => options.EnableDetailedErrors = false);
        services.AddSingleton<IRealtimeNotifier, HubRealtimeNotifier>();
        return services;
    }

    public static IEndpointRouteBuilder MapRealtime(this IEndpointRouteBuilder app)
    {
        app.MapHub<WorkstationHub>(WorkstationHub.Path);
        return app;
    }
}
