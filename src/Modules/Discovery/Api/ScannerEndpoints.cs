using Astryx.Infrastructure.Http;
using Astryx.Modules.Discovery.Application.Signals;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Astryx.Modules.Discovery.Api;

public sealed record SignalCountResponse(int Count);

/// <summary>D1–D4 (SYSTEM_DESIGN.md §11.4).</summary>
public static class ScannerEndpoints
{
    public static IEndpointRouteBuilder MapScannerEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/scanner/status", async (SignalQueries queries, CancellationToken cancellationToken) => Results.Ok(await queries.StatusAsync(cancellationToken)))
            .WithTags("Scanner").WithName("ScannerStatus").RequireAuthorization("Authenticated")
            .Produces<ScannerStatusDto>();

        var signals = app.MapGroup("/api/v1/signals").WithTags("Scanner").RequireAuthorization("Authenticated");

        signals.MapGet("/", async ([AsParameters] SignalFilterQuery query, SignalQueries queries, CancellationToken cancellationToken) =>
                (await queries.ListAsync(query.ToFilter(), cancellationToken)).Match(Results.Ok, error => error.ToProblem()))
            .WithName("ListSignals")
            .Produces<SignalPageDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        signals.MapGet("/count", async ([AsParameters] SignalFilterQuery query, SignalQueries queries, CancellationToken cancellationToken) =>
                (await queries.CountAsync(query.ToFilter(), cancellationToken)).Match(count => Results.Ok(new SignalCountResponse(count)), error => error.ToProblem()))
            .WithName("CountSignals")
            .Produces<SignalCountResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        signals.MapGet("/{id:guid}", async (Guid id, SignalQueries queries, CancellationToken cancellationToken) =>
                (await queries.GetAsync(id, cancellationToken)).Match(Results.Ok, error => error.ToProblem()))
            .WithName("GetSignal")
            .Produces<SignalDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}

/// <summary>D2/D3 query string.</summary>
public sealed record SignalFilterQuery(string? Type, string? Sector, string? MarketCap, string? Horizon, int? MinScore, string? Sort, string? Symbol, int? Limit, string? Cursor)
{
    public SignalFilter ToFilter() => new(Type, Sector, MarketCap, Horizon, MinScore, Sort, Symbol, Limit, Cursor);
}
