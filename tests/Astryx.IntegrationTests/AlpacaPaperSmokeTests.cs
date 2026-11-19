using Astryx.Infrastructure.Alpaca;
using Astryx.Modules.Paper.Infrastructure.Alpaca;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Astryx.IntegrationTests;

/// <summary>Runs only with real Alpaca paper keys in <c>ALPACA_KEY_ID</c> / <c>ALPACA_SECRET_KEY</c> (TASKS.md T-149); CI skips it.</summary>
public sealed class AlpacaPaperFactAttribute : FactAttribute
{
    public AlpacaPaperFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALPACA_KEY_ID")) || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALPACA_SECRET_KEY")))
        {
            Skip = "Set ALPACA_KEY_ID and ALPACA_SECRET_KEY (paper keys) to run the Alpaca paper smoke.";
        }
    }
}

/// <summary>
/// <see cref="AlpacaPaperFactAttribute"/> that also needs the regular session (09:30–16:00 New York, weekdays; holidays
/// fail fast at Alpaca rather than here), because it waits for a real fill.
/// </summary>
public sealed class AlpacaPaperMarketFactAttribute : FactAttribute
{
    public AlpacaPaperMarketFactAttribute()
    {
        var newYork = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));
        var open = newYork.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && newYork.TimeOfDay >= new TimeSpan(9, 35, 0) && newYork.TimeOfDay < new TimeSpan(15, 50, 0);
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALPACA_KEY_ID")) || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALPACA_SECRET_KEY")))
        {
            Skip = "Set ALPACA_KEY_ID and ALPACA_SECRET_KEY (paper keys) to run the Alpaca paper smoke.";
        }
        else if (!open)
        {
            Skip = "The filled round trip runs only during the regular session (09:35–15:50 New York).";
        }
    }
}

/// <summary>
/// The real Alpaca paper API through <see cref="AlpacaTradingClient"/> (SYSTEM_DESIGN.md §17.3–17.4, Q-22). It places a
/// one-share NVDA bracket whose buy stop sits far above the market, so it cannot fill, checks it, and cancels it.
/// </summary>
public class AlpacaPaperSmokeTests
{
    private static (IAlpacaTradingClient Client, AlpacaCredentials Credentials) Create()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var services = new ServiceCollection().AddAlpacaCore(configuration);
        services.AddSingleton<IAlpacaTradingClient, AlpacaTradingClient>();
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IAlpacaTradingClient>(), new AlpacaCredentials(Environment.GetEnvironmentVariable("ALPACA_KEY_ID")!, Environment.GetEnvironmentVariable("ALPACA_SECRET_KEY")!));
    }

    [AlpacaPaperFact]
    public async Task A_stop_entry_bracket_is_accepted_found_by_client_id_and_cancelled()
    {
        var (alpaca, credentials) = Create();
        var account = await alpaca.GetAccountAsync(credentials, default);
        account.Status.ShouldBe("ACTIVE");
        (await alpaca.GetAssetAsync(credentials, "NVDA", default))!.Tradable.ShouldBeTrue();

        var clientId = $"astryx-smoke-{Guid.NewGuid():N}";
        var placed = await alpaca.SubmitBracketAsync(credentials, new AlpacaBracketRequest("NVDA", 1m, "stop", StopPrice: 900m, LimitPrice: null, StopLoss: 850m, TakeProfit: 990m, clientId), default);
        try
        {
            placed.OrderClass.ShouldBe("bracket"); // Q-22: a bracket takes a buy-stop entry
            placed.Legs.Count.ShouldBe(2);
            (await alpaca.GetOrderByClientIdAsync(credentials, clientId, default))!.Id.ShouldBe(placed.Id);

            // Q-22: what a duplicate client_order_id gets back (recorded in SYSTEM_DESIGN.md §17.3).
            var duplicate = await Should.ThrowAsync<AlpacaApiException>(() => alpaca.SubmitBracketAsync(credentials, new AlpacaBracketRequest("NVDA", 1m, "stop", 900m, null, 850m, 990m, clientId), default));
            duplicate.Failure.ShouldBe(AlpacaFailure.Invalid);
        }
        finally
        {
            await alpaca.CancelOrderAsync(credentials, placed.Id, default);
        }

        var after = await alpaca.GetOrderAsync(credentials, placed.Id, default);
        after!.Status.ShouldBeOneOf("canceled", "pending_cancel");
    }

    /// <summary>
    /// A filled round trip (T-149): a one-share market bracket on SPY fills, shows up as a position and as a FILL
    /// activity, then the position is closed and the account is flat again. Use a paper account of its own: on the
    /// account the app is connected to, reconciliation would treat these trades as made outside Astryx and halt entries.
    /// </summary>
    [AlpacaPaperMarketFact]
    public async Task A_one_share_bracket_fills_appears_in_positions_and_activities_and_closes_flat()
    {
        var (alpaca, credentials) = Create();
        (await alpaca.ListPositionsAsync(credentials, default)).ShouldNotContain(p => p.Symbol == "SPY", "the smoke account must start flat in SPY");
        var started = DateTimeOffset.UtcNow.AddSeconds(-5L);

        var placed = await alpaca.SubmitBracketAsync(credentials, new AlpacaBracketRequest("SPY", 1m, "market", null, null, StopLoss: 1m, TakeProfit: 100_000m, $"astryx-smoke-{Guid.NewGuid():N}"), default);
        var filled = await Until(() => alpaca.GetOrderAsync(credentials, placed.Id, default), o => o is { IsFilled: true }, "the entry to fill");
        filled!.FilledQuantity.ShouldBe(1m);
        filled.FilledAvgPrice.ShouldNotBeNull().ShouldBeGreaterThan(0m);

        try
        {
            (await alpaca.ListPositionsAsync(credentials, default)).Single(p => p.Symbol == "SPY").Quantity.ShouldBe(1m);
            var fills = await Until(() => alpaca.ListFillsAsync(credentials, started, default), f => f.Any(x => x.OrderId == placed.Id), "the FILL activity");
            fills.Where(f => f.OrderId == placed.Id).Sum(f => f.Quantity).ShouldBe(1m);
        }
        finally
        {
            foreach (var leg in filled.Legs)
            {
                await alpaca.CancelOrderAsync(credentials, leg.Id, default); // the bracket's exits, before closing by hand
            }

            await alpaca.ClosePositionAsync(credentials, "SPY", default);
        }

        await Until(() => alpaca.ListPositionsAsync(credentials, default), p => p.All(x => x.Symbol != "SPY"), "the account to be flat in SPY");
    }

    private static async Task<T> Until<T>(Func<Task<T>> read, Func<T, bool> done, string what)
    {
        for (var deadline = DateTime.UtcNow.AddSeconds(60); ; await Task.Delay(1000))
        {
            var value = await read();
            if (done(value))
            {
                return value;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for {what}.");
            }
        }
    }
}
