using Astryx.Modules.MarketData.Application.Market;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Astryx.Modules.MarketData.Api;

/// <summary>M10 <c>GET /market/session</c> (SYSTEM_DESIGN.md §11.4).</summary>
public sealed record MarketSessionResponse(string Exchange, string State, DateOnly SessionDate, DateTimeOffset OpensAt, DateTimeOffset ClosesAt, bool IsHalfDay);

public static class MarketEndpoints
{
    public static IEndpointRouteBuilder MapMarketEndpoints(this IEndpointRouteBuilder app)
    {
        var market = app.MapGroup("/api/v1/market").WithTags("Market").RequireAuthorization("Authenticated");

        market.MapGet("/session", async (MarketSessionService sessions, CancellationToken cancellationToken) =>
        {
            var session = await sessions.GetAsync(cancellationToken);
            return Results.Ok(new MarketSessionResponse(session.Exchange, session.State.ToString().ToLowerInvariant(), session.SessionDate, session.OpensAt, session.ClosesAt, session.IsHalfDay));
        });

        return app;
    }
}
