using Astryx.Infrastructure.Http;
using Astryx.Infrastructure.Idempotency;
using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Paper.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Modules.Paper.Api;

/// <summary>T6–T8 (SYSTEM_DESIGN.md §11.7). Paper only: no request can reach a live venue (live trading stays disabled).</summary>
public static class PaperOrderEndpoints
{
    public static IEndpointRouteBuilder MapPaperOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/v1/paper/orders").WithTags("Paper orders").RequireAuthorization("Authenticated");

        orders.MapPost("/", async (PlaceOrderRequest request, HttpContext http, IServiceProvider services, CancellationToken cancellationToken) =>
            {
                // One transaction for the whole money movement (§15.3): every module context in this scope shares it.
                await using var scope = services.CreateAsyncScope();
                await using var unit = scope.ServiceProvider.GetRequiredService<ModuleUnitOfWork>();
                await unit.BeginAsync(cancellationToken);
                var key = http.Request.Headers[IdempotencyFilter.HeaderName].ToString();
                var result = await scope.ServiceProvider.GetRequiredService<PaperOrderService>().PlaceAsync(request, key, cancellationToken);
                if (!result.IsSuccess)
                {
                    return result.Error.ToProblem(); // the unit of work rolls back on dispose
                }

                await unit.CommitAsync(cancellationToken);
                return Results.Created($"/api/v1/paper/orders/{result.Value.Id}", result.Value);
            })
            .WithName("PlacePaperOrder")
            .RequireIdempotency()
            .RequireRateLimiting(RateLimitPolicies.Orders)
            .Produces<OrderDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        orders.MapGet("/{id:guid}", async (Guid id, PaperOrderService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(id, cancellationToken)).Match(Results.Ok, error => error.ToProblem()))
            .WithName("GetPaperOrder")
            .Produces<OrderDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        orders.MapGet("/", async (string? status, int? limit, PaperOrderService service, CancellationToken cancellationToken) =>
                (await service.ListAsync(status, limit, cancellationToken)).Match(Results.Ok, error => error.ToProblem()))
            .WithName("ListPaperOrders")
            .Produces<OrderPageDto>();

        return app;
    }
}
