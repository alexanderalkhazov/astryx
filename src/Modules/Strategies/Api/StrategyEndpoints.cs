using Astryx.Infrastructure.Http;
using Astryx.Modules.Strategies.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Astryx.Modules.Strategies.Api;

public sealed record StrategiesResponse(IReadOnlyList<StrategySummaryDto> Items);

public sealed record StrategyPeriodResponse(string Period, IReadOnlyList<StrategyPeriodDto> Items);

/// <summary>ST1, ST3–ST5 (SYSTEM_DESIGN.md §11.9). <c>{id}</c> is a strategy id or its code.</summary>
public static class StrategyEndpoints
{
    public static IEndpointRouteBuilder MapStrategyEndpoints(this IEndpointRouteBuilder app)
    {
        var strategies = app.MapGroup("/api/v1/strategies").WithTags("Strategies").RequireAuthorization("Authenticated");

        strategies.MapGet("/", async (StrategyCatalog catalog, CancellationToken cancellationToken) =>
                Results.Ok(new StrategiesResponse(await catalog.ListAsync(cancellationToken))))
            .WithName("ListStrategies")
            .Produces<StrategiesResponse>();

        strategies.MapGet("/summary", async (string? period, StrategyCatalog catalog, CancellationToken cancellationToken) =>
                (await catalog.SummaryAsync(period, cancellationToken)).Match(items => Results.Ok(new StrategyPeriodResponse(period ?? "mtd", items)), error => error.ToProblem()))
            .WithName("StrategySummary")
            .Produces<StrategyPeriodResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        strategies.MapGet("/{id}", async (string id, StrategyCatalog catalog, CancellationToken cancellationToken) =>
                (await catalog.GetAsync(id, cancellationToken)).Match(Results.Ok, error => error.ToProblem()))
            .WithName("GetStrategy")
            .Produces<StrategyDetailDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        strategies.MapGet("/{id}/runs/{runId}", async (string id, string runId, StrategyCatalog catalog, CancellationToken cancellationToken) =>
                (await catalog.RunAsync(id, runId, cancellationToken)).Match(Results.Ok, error => error.ToProblem()))
            .WithName("GetBacktestRun")
            .Produces<BacktestRunDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
