using Astryx.Contracts.Paper;
using Astryx.Infrastructure.Alpaca;
using Astryx.Modules.Paper.Domain;
using Astryx.Modules.Paper.Infrastructure.Alpaca;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Astryx.Modules.Paper.Application;

/// <summary>
/// <see cref="IBrokerReadiness"/> for Alpaca connections (SYSTEM_DESIGN.md §17.3, TASKS.md T-142): the connection is
/// live, the account may trade (<c>trading_blocked</c>, <c>account_blocked</c>), and the symbol maps to an
/// <c>active</c>, <c>tradable</c> Alpaca asset. Orders go by symbol, so the asset is cached in memory (an hour) rather
/// than persisted; account flags are cached for a minute so one assessment burst costs one account call.
/// </summary>
public sealed class BrokerReadinessService(BrokerCredentials credentials, IAlpacaTradingClient alpaca, IMemoryCache cache, ILogger<BrokerReadinessService> logger) : IBrokerReadiness
{
    private static readonly TimeSpan AssetTtl = TimeSpan.FromHours(1L);
    private static readonly TimeSpan AccountTtl = TimeSpan.FromMinutes(1L);

    public async Task<BrokerReadiness> GetAsync(Guid connectionId, string symbol, CancellationToken cancellationToken)
    {
        if (await credentials.OpenAsync(connectionId, cancellationToken) is not var (connection, keys) || connection.Status != BrokerConnectionStatus.Connected)
        {
            return new BrokerReadiness(Connected: false, CanPlaceOrders: false, Eligible: null);
        }

        try
        {
            var account = await cache.GetOrCreateAsync(("alpaca-account", connectionId), entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = AccountTtl;
                return alpaca.GetAccountAsync(keys, cancellationToken);
            });
            var canTrade = account is { TradingBlocked: false, AccountBlocked: false };
            var asset = await cache.GetOrCreateAsync(("alpaca-asset", symbol.ToUpperInvariant()), async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = AssetTtl;
                return await alpaca.GetAssetAsync(keys, symbol, cancellationToken) ?? new AlpacaAsset(symbol, "missing", false, false);
            });
            var eligible = asset is { Status: "active", Tradable: true };
            return new BrokerReadiness(true, canTrade, eligible, eligible ? null : asset!.Status == "missing" ? "Alpaca has no asset with this symbol." : "Not tradable at Alpaca.");
        }
        catch (AlpacaApiException ex) when (ex.Failure == AlpacaFailure.CredentialsInvalid)
        {
            return new BrokerReadiness(Connected: false, CanPlaceOrders: false, Eligible: null);
        }
        catch (AlpacaApiException ex)
        {
            logger.LogWarning("Alpaca readiness for {Symbol} unknown: {Reason}", symbol, ex.Message);
            return new BrokerReadiness(Connected: true, CanPlaceOrders: true, Eligible: null, "Alpaca could not be asked whether this symbol is tradable.");
        }
    }
}
