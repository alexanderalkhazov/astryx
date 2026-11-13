using Astryx.Infrastructure.Http;
using Astryx.Infrastructure.Idempotency;
using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Paper.Application;
using Astryx.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Modules.Paper.Api;

/// <summary>T9–T11 (SYSTEM_DESIGN.md §11.7, §15.5).</summary>
public static class PositionEndpoints
{
    public static IEndpointRouteBuilder MapPositionEndpoints(this IEndpointRouteBuilder app)
    {
        var positions = app.MapGroup("/api/v1/positions").WithTags("Positions").RequireAuthorization("Authenticated");

        positions.MapPost("/{id:guid}/close-preview", async (Guid id, PositionActions actions, CancellationToken cancellationToken) =>
                (await actions.PreviewAsync(id, cancellationToken)).Match(Results.Ok, error => error.ToProblem()))
            .WithName("PreviewClosePosition")
            .Produces<ClosePreviewDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        positions.MapPost("/{id:guid}/close", (Guid id, HttpContext http, IServiceProvider services, CancellationToken cancellationToken) =>
                InTransactionAsync(services, sp => sp.GetRequiredService<PositionActions>().CloseAsync(id, http.Request.Headers[IdempotencyFilter.HeaderName].ToString(), cancellationToken), Results.Ok, cancellationToken))
            .WithName("ClosePosition")
            .RequireIdempotency()
            .RequireRateLimiting(RateLimitPolicies.Orders)
            .Produces<ClosePositionDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapPost("/api/v1/paper/orders/{id:guid}/cancel", (Guid id, IServiceProvider services, CancellationToken cancellationToken) =>
                InTransactionAsync(services, sp => sp.GetRequiredService<PositionActions>().CancelAsync(id, cancellationToken), Results.Ok, cancellationToken))
            .WithTags("Paper orders")
            .RequireAuthorization("Authenticated")
            .WithName("CancelPaperOrder")
            .RequireIdempotency()
            .Produces<OrderDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    /// <summary>Runs a money movement in one <see cref="ModuleUnitOfWork"/> on a fresh scope; commits only on success.</summary>
    internal static async Task<IResult> InTransactionAsync<T>(IServiceProvider services, Func<IServiceProvider, Task<Result<T>>> action, Func<T, IResult> onSuccess, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var unit = scope.ServiceProvider.GetRequiredService<ModuleUnitOfWork>();
        await unit.BeginAsync(cancellationToken);
        var result = await action(scope.ServiceProvider);
        if (!result.IsSuccess)
        {
            return result.Error.ToProblem();
        }

        await unit.CommitAsync(cancellationToken);
        return onSuccess(result.Value);
    }
}
