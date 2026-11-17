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
}
