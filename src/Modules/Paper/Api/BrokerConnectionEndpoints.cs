using Astryx.Infrastructure.Http;
using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Paper.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Modules.Paper.Api;

/// <summary>N7–N9 (SYSTEM_DESIGN.md §11, §17.6, TASKS.md T-141). Paper connections only while live trading is disabled.</summary>
public static class BrokerConnectionEndpoints
{
    public static IEndpointRouteBuilder MapBrokerConnectionEndpoints(this IEndpointRouteBuilder app)
    {
        var connections = app.MapGroup("/api/v1/broker/connections").WithTags("Broker connections").RequireAuthorization("Authenticated");

        connections.MapGet("/", async (BrokerConnectionService service, CancellationToken cancellationToken) => Results.Ok(await service.ListAsync(cancellationToken)))
            .WithName("ListBrokerConnections")
            .Produces<BrokerConnectionPageDto>();

        connections.MapPost("/", async (ConnectBrokerRequest request, IServiceProvider services, CancellationToken cancellationToken) =>
            {
                // The connection and the portfolio it attaches commit together.
                await using var scope = services.CreateAsyncScope();
                await using var unit = scope.ServiceProvider.GetRequiredService<ModuleUnitOfWork>();
                await unit.BeginAsync(cancellationToken);
                var result = await scope.ServiceProvider.GetRequiredService<BrokerConnectionService>().ConnectAsync(request, cancellationToken);
                if (!result.IsSuccess)
                {
                    return result.Error.ToProblem();
                }

                await unit.CommitAsync(cancellationToken);
                return Results.Created($"/api/v1/broker/connections/{result.Value.Id}", result.Value);
            })
            .WithName("ConnectBroker")
            .RequireRateLimiting(RateLimitPolicies.AuthStrict) // credential attempts are limited like sign-ins
            .Produces<BrokerConnectionDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        connections.MapDelete("/{id:guid}", async (Guid id, IServiceProvider services, CancellationToken cancellationToken) =>
            {
                await using var scope = services.CreateAsyncScope();
                await using var unit = scope.ServiceProvider.GetRequiredService<ModuleUnitOfWork>();
                await unit.BeginAsync(cancellationToken);
                var result = await scope.ServiceProvider.GetRequiredService<BrokerConnectionService>().DisconnectAsync(id, cancellationToken);
                if (!result.IsSuccess)
                {
                    return result.Error.ToProblem();
                }

                await unit.CommitAsync(cancellationToken);
                return Results.NoContent();
            })
            .WithName("DisconnectBroker")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
