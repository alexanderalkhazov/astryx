using Astryx.Infrastructure.Http;
using Astryx.Modules.Discovery.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Astryx.Modules.Discovery.Api;

public sealed record SearchItemsResponse(IReadOnlyList<InstrumentSearchItem> Items);

public sealed record AddRecentRequest(string Symbol);

/// <summary>M1–M3 (SYSTEM_DESIGN.md §11.4).</summary>
public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        var search = app.MapGroup("/api/v1/search").WithTags("Search").RequireAuthorization("Authenticated");

        search.MapGet("/", async (string? q, int? limit, SearchService service, CancellationToken cancellationToken) =>
                (await service.SearchAsync(q, limit ?? 20, cancellationToken)).Match(items => Results.Ok(new SearchItemsResponse(items)), error => error.ToProblem()))
            .Produces<SearchItemsResponse>().ProducesProblem(StatusCodes.Status400BadRequest);

        search.MapGet("/recent", async (SearchService service, CancellationToken cancellationToken) =>
                Results.Ok(new SearchItemsResponse(await service.RecentAsync(cancellationToken))))
            .Produces<SearchItemsResponse>();

        search.MapPost("/recent", async (AddRecentRequest request, SearchService service, CancellationToken cancellationToken) =>
            {
                var result = await service.AddRecentAsync(request.Symbol, cancellationToken);
                return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
            })
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
