using Astryx.Modules.MarketData.Infrastructure.Providers.Fake;

namespace Astryx.Api.Dev;

/// <summary>
/// The design's "Prototype controls" (SYSTEM_DESIGN.md §11.11). Mapped only in Development with
/// <c>Features:DevFaultInjection=true</c>; absent everywhere else. Scanner faults join in M3.
/// </summary>
public static class DevEndpoints
{
    public sealed record FaultsRequest(string? MarketData);

    public sealed record FaultsResponse(string MarketData, DateTimeOffset? FrozenAt);

    public static bool IsEnabled(IHostEnvironment environment, IConfiguration configuration) =>
        environment.IsDevelopment() && configuration.GetValue<bool>("Features:DevFaultInjection");

    public static IEndpointRouteBuilder MapDevEndpoints(this IEndpointRouteBuilder app)
    {
        var dev = app.MapGroup("/dev").ExcludeFromDescription();

        dev.MapGet("/faults", (MarketDataFaults faults) => Results.Ok(Describe(faults)));

        dev.MapPost("/faults", (FaultsRequest request, MarketDataFaults faults) =>
        {
            if (request.MarketData is { } value)
            {
                if (!TryParse(value, out var mode))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status422UnprocessableEntity,
                        title: "Unknown fault mode",
                        detail: "marketData must be one of: ok, down, stale, bad_tick.",
                        extensions: new Dictionary<string, object?> { ["code"] = "validation_failed" });
                }

                faults.Set(mode);
            }

            return Results.Ok(Describe(faults));
        });

        return app;
    }

    private static FaultsResponse Describe(MarketDataFaults faults) => new(Format(faults.Mode), faults.FrozenAt);

    private static bool TryParse(string value, out MarketDataFaultMode mode)
    {
        mode = value switch
        {
            "ok" => MarketDataFaultMode.Ok,
            "down" => MarketDataFaultMode.Down,
            "stale" => MarketDataFaultMode.Stale,
            "bad_tick" => MarketDataFaultMode.BadTick,
            _ => (MarketDataFaultMode)(-1),
        };
        return Enum.IsDefined(mode);
    }

    private static string Format(MarketDataFaultMode mode) => mode switch
    {
        MarketDataFaultMode.Down => "down",
        MarketDataFaultMode.Stale => "stale",
        MarketDataFaultMode.BadTick => "bad_tick",
        _ => "ok",
    };
}
