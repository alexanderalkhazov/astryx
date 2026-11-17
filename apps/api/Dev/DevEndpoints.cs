using Astryx.Infrastructure.Jobs;
using Astryx.Modules.Discovery.Application.Scanner;
using Astryx.Modules.MarketData.Application.Ingestion;
using Astryx.Modules.MarketData.Application.Providers;
using Astryx.Modules.MarketData.Infrastructure.Persistence;
using Astryx.Modules.MarketData.Infrastructure.Providers.Fake;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Api.Dev;

/// <summary>
/// The design's "Prototype controls" (SYSTEM_DESIGN.md §11.11). Mapped only in Development with
/// <c>Features:DevFaultInjection=true</c>; absent everywhere else. Scanner faults join in M3.
/// </summary>
public static class DevEndpoints
{
    /// <param name="Session"><c>open</c> forces a regular session now (E2E at any hour); <c>real</c> returns to the calendar.</param>
    /// <param name="Prices">Fixed last prices for the fake provider (E2E: NVDA above its buy stop so the entry fills); an empty object clears them.</param>
    /// <param name="Scanner"><c>failing</c> makes scanner runs fail (Discover's scanner error); <c>ok</c> restores them. Either rescans at once.</param>
    public sealed record FaultsRequest(string? MarketData, string? Session = null, Dictionary<string, decimal>? Prices = null, string? Scanner = null);

    public sealed record FaultsResponse(string MarketData, DateTimeOffset? FrozenAt, string Session, IReadOnlyDictionary<string, decimal> Prices, string Scanner);

    public static bool IsEnabled(IHostEnvironment environment, IConfiguration configuration) =>
        environment.IsDevelopment() && configuration.GetValue<bool>("Features:DevFaultInjection");

    public static IEndpointRouteBuilder MapDevEndpoints(this IEndpointRouteBuilder app)
    {
        var dev = app.MapGroup("/dev").ExcludeFromDescription();

        dev.MapGet("/faults", (MarketDataFaults faults, ScannerFaults scanner) => Results.Ok(Describe(faults, scanner)));

        dev.MapPost("/faults", async (FaultsRequest request, MarketDataFaults faults, ScannerFaults scanner, IServiceProvider services, CancellationToken cancellationToken) =>
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

            if (request.Session is { } session)
            {
                if (session is not ("open" or "real"))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status422UnprocessableEntity,
                        title: "Unknown session mode",
                        detail: "session must be one of: open, real.",
                        extensions: new Dictionary<string, object?> { ["code"] = "validation_failed" });
                }

                faults.SessionForcedOpen = session == "open";
            }

            if (request.Prices is { } prices)
            {
                if (prices.Values.Any(p => p <= 0))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status422UnprocessableEntity,
                        title: "Invalid price",
                        detail: "prices must be positive.",
                        extensions: new Dictionary<string, object?> { ["code"] = "validation_failed" });
                }

                faults.PriceOverrides.Clear();
                foreach (var (symbol, price) in prices)
                {
                    faults.PriceOverrides[symbol] = price;
                }
            }

            if (request.Scanner is { } scannerMode)
            {
                if (scannerMode is not ("ok" or "failing"))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status422UnprocessableEntity,
                        title: "Unknown scanner mode",
                        detail: "scanner must be one of: ok, failing.",
                        extensions: new Dictionary<string, object?> { ["code"] = "validation_failed" });
                }

                scanner.Failing = scannerMode == "failing";
                await using var scope = services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ScannerRunHandler>().HandleAsync(new ScannerRunJob("manual"), new JobContext(Guid.NewGuid(), 1, 1, null), cancellationToken);
            }

            return Results.Ok(Describe(faults, scanner));
        });

        // E2E (T-110): the fake provider shapes its history around the current session, but stored bars keep the shape
        // of the day they were synced, so after a session or two the fixture setups (NVDA's breakout) are gone.
        // Replaces the stored fake daily bars and cached indicators with history for today, then rescans.
        dev.MapPost("/market/reseed", async (IServiceProvider services, CancellationToken cancellationToken) =>
        {
            await using var scope = services.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            if (sp.GetRequiredService<IMarketDataProvider>().Code != "fake")
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Not the fake provider",
                    detail: "Reseeding only replaces fake market data.",
                    extensions: new Dictionary<string, object?> { ["code"] = "conflict" });
            }

            var db = sp.GetRequiredService<MarketDataDbContext>();
            var bars = await db.Database.ExecuteSqlRawAsync("DELETE FROM market.price_bars WHERE source = 'fake'", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM market.indicator_snapshots", cancellationToken);
            await db.Database.ExecuteSqlRawAsync($"DELETE FROM market.provider_sync_state WHERE dataset = '{SyncDataset.EodBars}'", cancellationToken);

            var context = new JobContext(Guid.NewGuid(), 1, 1, null);
            await sp.GetRequiredService<EodBarSyncHandler>().HandleAsync(new EodBarSyncJob(), context, cancellationToken);
            await sp.GetRequiredService<ScannerRunHandler>().HandleAsync(new ScannerRunJob("manual"), context, cancellationToken);
            var stored = await db.PriceBars.CountAsync(cancellationToken);
            return Results.Ok(new { removedBars = bars, storedBars = stored });
        });

        // Switching a database from the fake provider to a real one (T-147): fake history would otherwise be extended
        // with real bars. Removes every fake-sourced market row and the signals scanned from it, then re-lists the
        // universe; the recurring syncs fill bars, quotes and corporate actions from the real provider.
        dev.MapPost("/market/purge-fake", async (IServiceProvider services, CancellationToken cancellationToken) =>
        {
            await using var scope = services.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            if (sp.GetRequiredService<IMarketDataProvider>().Code == "fake")
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Still the fake provider",
                    detail: "Set MarketData:Provider to a real provider before purging fake data.",
                    extensions: new Dictionary<string, object?> { ["code"] = "conflict" });
            }

            var db = sp.GetRequiredService<MarketDataDbContext>();
            var removed = new Dictionary<string, int>(StringComparer.Ordinal);
            await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
            {
                removed["priceBars"] = await db.Database.ExecuteSqlRawAsync("DELETE FROM market.price_bars WHERE source = 'fake'", cancellationToken);
                removed["quotes"] = await db.Database.ExecuteSqlRawAsync("DELETE FROM market.quotes WHERE source = 'fake'", cancellationToken);
                removed["corporateActions"] = await db.Database.ExecuteSqlRawAsync("DELETE FROM market.corporate_actions WHERE source = 'fake'", cancellationToken);
                removed["fundamentals"] = await db.Database.ExecuteSqlRawAsync("DELETE FROM market.fundamentals WHERE source = 'fake'", cancellationToken);
                await db.Database.ExecuteSqlRawAsync("DELETE FROM market.instrument_news WHERE article_id IN (SELECT id FROM market.news_articles WHERE provider = 'fake')", cancellationToken);
                removed["news"] = await db.Database.ExecuteSqlRawAsync("DELETE FROM market.news_articles WHERE provider = 'fake'", cancellationToken);
                removed["indicatorSnapshots"] = await db.Database.ExecuteSqlRawAsync("DELETE FROM market.indicator_snapshots", cancellationToken);
                await db.Database.ExecuteSqlRawAsync("DELETE FROM market.provider_sync_state", cancellationToken);
                removed["signalsExpired"] = await db.Database.ExecuteSqlRawAsync("UPDATE discovery.signals SET status = 'expired' WHERE status = 'active'", cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            var context = new JobContext(Guid.NewGuid(), 1, 1, null);
            await sp.GetRequiredService<InstrumentSyncHandler>().HandleAsync(new InstrumentSyncJob(), context, cancellationToken);
            await sp.GetRequiredService<CalendarSyncHandler>().HandleAsync(new CalendarSyncJob(), context, cancellationToken);
            var active = await db.Instruments.CountAsync(i => i.IsActive, cancellationToken);
            return Results.Ok(new { removed, activeInstruments = active });
        });

        return app;
    }

    private static FaultsResponse Describe(MarketDataFaults faults, ScannerFaults scanner) => new(Format(faults.Mode), faults.FrozenAt, faults.SessionForcedOpen ? "open" : "real", faults.PriceOverrides.ToDictionary(p => p.Key, p => p.Value), scanner.Failing ? "failing" : "ok");

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
