using Astryx.Infrastructure.Http;
using Astryx.Infrastructure.Security;
using Astryx.Modules.Portfolio.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Astryx.Modules.Portfolio.Api;

/// <summary>P1, P2 and P5–P9 (SYSTEM_DESIGN.md §11.8).</summary>
public static class PortfolioEndpoints
{
    public static IEndpointRouteBuilder MapPortfolioEndpoints(this IEndpointRouteBuilder app)
    {
        var portfolio = app.MapGroup("/api/v1/portfolio").WithTags("Portfolio").RequireAuthorization("Authenticated");

        portfolio.MapGet("/", async (ValuationService service, CancellationToken ct) => (await service.GetAsync(ct)).Match(Results.Ok, e => e.ToProblem()))
            .WithName("GetPortfolio").Produces<PortfolioDto>().ProducesProblem(StatusCodes.Status404NotFound);

        // P10 sync: an Alpaca portfolio pulls from Alpaca first (T-145); a simulated one only revalues at the latest quotes.
        portfolio.MapPost("/sync", async (ValuationService service, ICurrentUser user, Astryx.Contracts.Paper.IBrokerSync broker, CancellationToken ct) =>
            {
                if (user.UserId is { } userId)
                {
                    await broker.SyncUserAsync(userId, ct); // an Alpaca portfolio pulls Alpaca's orders, cash and positions first (§17.7)
                }

                return (await service.GetAsync(ct)).Match(Results.Ok, e => e.ToProblem());
            })
            .WithName("SyncPortfolio").RequireRateLimiting(RateLimitPolicies.Sync).Produces<PortfolioDto>();

        portfolio.MapGet("/positions", async (string? status, int? limit, string? cursor, ValuationService service, CancellationToken ct) =>
                (await service.PositionsAsync(status, limit, cursor, ct)).Match(Results.Ok, e => e.ToProblem()))
            .WithName("ListPortfolioPositions").Produces<PositionPageDto>();

        portfolio.MapGet("/performance", async (string? range, ValuationService service, CancellationToken ct) =>
                (await service.PerformanceAsync(range, ct)).Match(Results.Ok, e => e.ToProblem()))
            .WithName("GetPortfolioPerformance").Produces<PerformanceDto>().ProducesProblem(StatusCodes.Status400BadRequest);

        portfolio.MapGet("/trades", async (int? limit, string? cursor, ValuationService service, CancellationToken ct) =>
                (await service.TradesAsync(limit, cursor, ct)).Match(Results.Ok, e => e.ToProblem()))
            .WithName("ListClosedTrades").Produces<ClosedTradePageDto>();

        portfolio.MapGet("/allocation", async (ExposureService service, CancellationToken ct) => (await service.AllocationAsync(ct)).Match(Results.Ok, e => e.ToProblem()))
            .WithName("GetPortfolioAllocation").Produces<AllocationDto>();

        portfolio.MapGet("/exposure", async (ExposureService service, CancellationToken ct) => (await service.ExposureAsync(ct)).Match(Results.Ok, e => e.ToProblem()))
            .WithName("GetPortfolioExposure").Produces<ExposureDto>();

        portfolio.MapGet("/attention", async (ExposureService service, CancellationToken ct) => (await service.AttentionAsync(ct)).Match(Results.Ok, e => e.ToProblem()))
            .WithName("GetPortfolioAttention").Produces<AttentionDto>();

        var positions = app.MapGroup("/api/v1/positions").WithTags("Positions").RequireAuthorization("Authenticated");
        positions.MapGet("/{id:guid}", async (Guid id, PositionDetailService service, CancellationToken ct) => (await service.GetAsync(id, ct)).Match(Results.Ok, e => e.ToProblem()))
            .WithName("GetPosition").Produces<PositionDetailDto>().ProducesProblem(StatusCodes.Status404NotFound);
        positions.MapGet("/by-symbol/{symbol}", async (string symbol, PositionDetailService service, CancellationToken ct) => (await service.BySymbolAsync(symbol, ct)).Match(Results.Ok, e => e.ToProblem()))
            .WithName("GetPositionBySymbol").Produces<PositionDetailDto>().ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
