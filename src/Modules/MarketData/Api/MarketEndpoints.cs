using Astryx.Infrastructure.Http;
using Astryx.Modules.MarketData.Application.Market;
using Astryx.Modules.MarketData.Application.Providers;
using Astryx.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Astryx.Modules.MarketData.Api;

/// <summary>M10 <c>GET /market/session</c> (SYSTEM_DESIGN.md §11.4).</summary>
public sealed record MarketSessionResponse(string Exchange, string State, DateOnly SessionDate, DateTimeOffset OpensAt, DateTimeOffset ClosesAt, bool IsHalfDay);

public sealed record ItemsResponse<T>(IReadOnlyList<T> Items);

/// <summary>Market endpoints M4–M6 and M8–M11 (SYSTEM_DESIGN.md §11.4). M7 (indicators) arrives with the Quant engine.</summary>
public static class MarketEndpoints
{
    public const int MaxNews = 20;

    public static IEndpointRouteBuilder MapMarketEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").WithTags("Market").RequireAuthorization("Authenticated").AddEndpointFilter(MapProviderOutage);

        api.MapGet("/market/session", async (MarketSessionService sessions, CancellationToken cancellationToken) =>
        {
            var session = await sessions.GetAsync(cancellationToken);
            return Results.Ok(new MarketSessionResponse(session.Exchange, session.State.ToString().ToLowerInvariant(), session.SessionDate, session.OpensAt, session.ClosesAt, session.IsHalfDay));
        });

        api.MapGet("/market/breadth", async (MarketQueries market, CancellationToken cancellationToken) =>
            Respond(await market.BreadthAsync(cancellationToken)));

        api.MapGet("/instruments/{symbol}", async (string symbol, MarketQueries market, CancellationToken cancellationToken) =>
            Respond(await market.InstrumentAsync(symbol, cancellationToken)));

        api.MapGet("/market/{symbol}/quote", async (string symbol, MarketQueries market, CancellationToken cancellationToken) =>
            Respond(await market.QuoteAsync(symbol, cancellationToken)));

        api.MapGet("/market/{symbol}/bars", async (string symbol, string? timeframe, string? interval, DateOnly? from, DateOnly? to, int? limit, MarketQueries market, CancellationToken cancellationToken) =>
            Respond(await market.BarsAsync(symbol, new BarsQuery(timeframe ?? (interval is null ? "3M" : null), interval, from, to, limit), cancellationToken)));

        api.MapGet("/instruments/{symbol}/fundamentals", async (string symbol, MarketQueries market, CancellationToken cancellationToken) =>
            Respond(await market.FundamentalsAsync(symbol, cancellationToken)));

        api.MapGet("/instruments/{symbol}/news", async (string symbol, int? limit, MarketQueries market, CancellationToken cancellationToken) =>
            limit is < 1 or > MaxNews
                ? Error.Validation($"limit must be between 1 and {MaxNews}.").ToProblem()
                : (await market.NewsAsync(symbol, limit ?? 10, cancellationToken)).Match(items => Results.Ok(new ItemsResponse<NewsItemDto>(items)), error => error.ToProblem()));

        return app;
    }

    private static IResult Respond<T>(Result<T> result) => result.Match(value => Results.Ok(value), error => error.ToProblem());

    /// <summary>A provider outage on data that is not stored yet is 503 <c>market_data_unavailable</c>.</summary>
    private static async ValueTask<object?> MapProviderOutage(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (MarketDataUnavailableException)
        {
            return new Error("market_data_unavailable", "Market data is temporarily unavailable. Try again shortly.").ToProblem();
        }
    }
}
