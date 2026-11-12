using Astryx.Infrastructure.Http;
using Astryx.Modules.Quant.Application.Analysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Astryx.Modules.Quant.Api;

/// <summary>M7 (SYSTEM_DESIGN.md §11.4).</summary>
public static class IndicatorEndpoints
{
    public static IEndpointRouteBuilder MapIndicatorEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/market/{symbol}/indicators", async (string symbol, string? interval, IndicatorSnapshotService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(symbol, interval, cancellationToken)).Match(Results.Ok, error => error.ToProblem()))
            .WithTags("Market")
            .WithName("GetIndicators")
            .RequireAuthorization("Authenticated")
            .Produces<IndicatorSnapshot>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
