using System.Globalization;
using System.Text.Json;
using Astryx.Infrastructure.Alpaca;
using Microsoft.Extensions.Options;

namespace Astryx.Modules.Paper.Infrastructure.Alpaca;

public sealed record AlpacaAccount(string AccountNumber, string Status, decimal Cash, decimal Equity, decimal BuyingPower, bool TradingBlocked, bool AccountBlocked);

public sealed record AlpacaAsset(string Symbol, string Status, bool Tradable, bool Fractionable);

public sealed record AlpacaPosition(string Symbol, string AssetId, decimal Quantity, decimal AvgEntryPrice, decimal? CurrentPrice);

/// <summary>One <c>FILL</c> account activity: a full or partial execution of <paramref name="OrderId"/>.</summary>
public sealed record AlpacaFill(string Id, string OrderId, string Symbol, string Side, decimal Quantity, decimal Price, DateTimeOffset At);

/// <summary>An order as Alpaca reports it. <see cref="Legs"/> holds a bracket's take-profit and stop-loss orders.</summary>
public sealed record AlpacaOrder(
    string Id,
    string ClientOrderId,
    string Status,
    string Symbol,
    string Side,
    string Type,
    decimal Quantity,
    decimal FilledQuantity,
    decimal? FilledAvgPrice,
    DateTimeOffset? FilledAt,
    decimal? StopPrice,
    decimal? LimitPrice,
    string OrderClass,
    IReadOnlyList<AlpacaOrder> Legs)
{
    public bool IsFilled => Status == "filled";

    /// <summary>Alpaca will not fill it any more (§17.3 statuses).</summary>
    public bool IsClosedUnfilled => Status is "canceled" or "expired" or "rejected" or "done_for_day" && FilledQuantity == 0;
}

/// <summary>
/// A bracket entry (§17.4): whole shares, Alpaca manages the stop and target as an OCO pair. Sent GTC: Alpaca gives the
/// legs the entry's time in force, so a day bracket would drop the stop at the close. The entry's day lifetime is kept by
/// <c>AlpacaExecution</c>, which cancels it once the session ends unfilled (verified in Paper 2026-11-19).
/// </summary>
public sealed record AlpacaBracketRequest(string Symbol, decimal Quantity, string Type, decimal? StopPrice, decimal? LimitPrice, decimal StopLoss, decimal TakeProfit, string ClientOrderId);

/// <summary>
/// Alpaca's Trading API for one user's paper account (SYSTEM_DESIGN.md §17.3, TASKS.md T-140). Every call takes the
/// connection's credentials; the environment is always paper while the §17.9 gates are open. There are deliberately no
/// funding, transfer or journal methods (enforced by an architecture test).
/// </summary>
public interface IAlpacaTradingClient
{
    Task<AlpacaAccount> GetAccountAsync(AlpacaCredentials credentials, CancellationToken cancellationToken);

    Task<AlpacaAsset?> GetAssetAsync(AlpacaCredentials credentials, string symbol, CancellationToken cancellationToken);

    Task<AlpacaOrder> SubmitBracketAsync(AlpacaCredentials credentials, AlpacaBracketRequest request, CancellationToken cancellationToken);

    Task<AlpacaOrder?> GetOrderAsync(AlpacaCredentials credentials, string orderId, CancellationToken cancellationToken);

    Task<AlpacaOrder?> GetOrderByClientIdAsync(AlpacaCredentials credentials, string clientOrderId, CancellationToken cancellationToken);

    Task CancelOrderAsync(AlpacaCredentials credentials, string orderId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AlpacaPosition>> ListPositionsAsync(AlpacaCredentials credentials, CancellationToken cancellationToken);

    /// <summary>Market-sells the whole position; returns the closing order.</summary>
    Task<AlpacaOrder> ClosePositionAsync(AlpacaCredentials credentials, string symbol, CancellationToken cancellationToken);

    /// <summary>Executions after <paramref name="after"/>, oldest first (<c>GET /v2/account/activities/FILL</c>), for reconciliation (§17.7).</summary>
    Task<IReadOnlyList<AlpacaFill>> ListFillsAsync(AlpacaCredentials credentials, DateTimeOffset after, CancellationToken cancellationToken);
}

public sealed class AlpacaTradingClient(IHttpClientFactory httpFactory, AlpacaRateLimiter limiter, IOptions<AlpacaOptions> options) : IAlpacaTradingClient
{
    private Uri Trading(string path) => new(options.Value.TradingBaseUrl(AlpacaEnvironment.Paper), path);

    private async Task<JsonDocument> SendAsync(AlpacaCredentials credentials, HttpMethod method, string path, object? body, CancellationToken cancellationToken) =>
        await AlpacaHttp.SendAsync(httpFactory.CreateClient(AlpacaHttpClient.Name), limiter, credentials, method, Trading(path), body, cancellationToken);

    public async Task<AlpacaAccount> GetAccountAsync(AlpacaCredentials credentials, CancellationToken cancellationToken)
    {
        using var json = await SendAsync(credentials, HttpMethod.Get, "v2/account", null, cancellationToken);
        var a = json.RootElement;
        return new AlpacaAccount(
            a.String("account_number") ?? string.Empty,
            a.String("status") ?? "unknown",
            a.Decimal("cash") ?? 0m,
            a.Decimal("equity") ?? 0m,
            a.Decimal("buying_power") ?? 0m,
            a.TryGetProperty("trading_blocked", out var tb) && tb.ValueKind == JsonValueKind.True,
            a.TryGetProperty("account_blocked", out var ab) && ab.ValueKind == JsonValueKind.True);
    }

    public async Task<AlpacaAsset?> GetAssetAsync(AlpacaCredentials credentials, string symbol, CancellationToken cancellationToken)
    {
        try
        {
            using var json = await SendAsync(credentials, HttpMethod.Get, $"v2/assets/{Uri.EscapeDataString(symbol)}", null, cancellationToken);
            var a = json.RootElement;
            return new AlpacaAsset(a.String("symbol") ?? symbol, a.String("status") ?? "inactive", Bool(a, "tradable"), Bool(a, "fractionable"));
        }
        catch (AlpacaApiException ex) when (ex.Failure == AlpacaFailure.NotFound)
        {
            return null;
        }
    }

    public async Task<AlpacaOrder> SubmitBracketAsync(AlpacaCredentials credentials, AlpacaBracketRequest request, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["symbol"] = request.Symbol,
            ["qty"] = Text(request.Quantity),
            ["side"] = "buy",
            ["type"] = request.Type,
            ["time_in_force"] = "gtc",
            ["stop_price"] = request.StopPrice is { } stop ? Text(stop) : null,
            ["limit_price"] = request.LimitPrice is { } limit ? Text(limit) : null,
            ["order_class"] = "bracket",
            ["take_profit"] = new Dictionary<string, string> { ["limit_price"] = Text(request.TakeProfit) },
            ["stop_loss"] = new Dictionary<string, string> { ["stop_price"] = Text(request.StopLoss) },
            ["client_order_id"] = request.ClientOrderId,
        };
        foreach (var key in body.Where(p => p.Value is null).Select(p => p.Key).ToList())
        {
            body.Remove(key); // send only the prices this order type uses
        }

        using var json = await SendAsync(credentials, HttpMethod.Post, "v2/orders", body, cancellationToken);
        return ToOrder(json.RootElement);
    }

    public async Task<AlpacaOrder?> GetOrderAsync(AlpacaCredentials credentials, string orderId, CancellationToken cancellationToken)
    {
        try
        {
            using var json = await SendAsync(credentials, HttpMethod.Get, $"v2/orders/{Uri.EscapeDataString(orderId)}?nested=true", null, cancellationToken);
            return ToOrder(json.RootElement);
        }
        catch (AlpacaApiException ex) when (ex.Failure == AlpacaFailure.NotFound)
        {
            return null;
        }
    }

    public async Task<AlpacaOrder?> GetOrderByClientIdAsync(AlpacaCredentials credentials, string clientOrderId, CancellationToken cancellationToken)
    {
        try
        {
            using var json = await SendAsync(credentials, HttpMethod.Get, $"v2/orders:by_client_order_id?client_order_id={Uri.EscapeDataString(clientOrderId)}&nested=true", null, cancellationToken);
            return ToOrder(json.RootElement);
        }
        catch (AlpacaApiException ex) when (ex.Failure == AlpacaFailure.NotFound)
        {
            return null;
        }
    }

    public async Task CancelOrderAsync(AlpacaCredentials credentials, string orderId, CancellationToken cancellationToken)
    {
        try
        {
            using var _ = await SendAsync(credentials, HttpMethod.Delete, $"v2/orders/{Uri.EscapeDataString(orderId)}", null, cancellationToken);
        }
        catch (AlpacaApiException ex) when (ex.Failure is AlpacaFailure.NotFound or AlpacaFailure.Invalid)
        {
            // already filled, cancelled or gone: nothing left to cancel
        }
    }

    public async Task<IReadOnlyList<AlpacaPosition>> ListPositionsAsync(AlpacaCredentials credentials, CancellationToken cancellationToken)
    {
        using var json = await SendAsync(credentials, HttpMethod.Get, "v2/positions", null, cancellationToken);
        return json.RootElement.EnumerateArray()
            .Select(p => new AlpacaPosition(p.String("symbol") ?? string.Empty, p.String("asset_id") ?? string.Empty, p.Decimal("qty") ?? 0m, p.Decimal("avg_entry_price") ?? 0m, p.Decimal("current_price")))
            .ToList();
    }

    public async Task<AlpacaOrder> ClosePositionAsync(AlpacaCredentials credentials, string symbol, CancellationToken cancellationToken)
    {
        using var json = await SendAsync(credentials, HttpMethod.Delete, $"v2/positions/{Uri.EscapeDataString(symbol)}", null, cancellationToken);
        return ToOrder(json.RootElement);
    }

    /// <summary>At most this many pages of 100 per call; a busier day is reconciled on the next pass.</summary>
    private const int MaxFillPages = 10;

    public async Task<IReadOnlyList<AlpacaFill>> ListFillsAsync(AlpacaCredentials credentials, DateTimeOffset after, CancellationToken cancellationToken)
    {
        var fills = new List<AlpacaFill>();
        string? pageToken = null;
        for (var page = 0; page < MaxFillPages; page++)
        {
            var path = $"v2/account/activities/FILL?after={Uri.EscapeDataString(after.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))}&direction=asc&page_size=100"
                + (pageToken is null ? string.Empty : $"&page_token={Uri.EscapeDataString(pageToken)}");
            using var json = await SendAsync(credentials, HttpMethod.Get, path, null, cancellationToken);
            var batch = json.RootElement.EnumerateArray().Select(a => new AlpacaFill(
                a.String("id") ?? string.Empty, a.String("order_id") ?? string.Empty, a.String("symbol") ?? string.Empty, a.String("side") ?? string.Empty,
                a.Decimal("qty") ?? 0m, a.Decimal("price") ?? 0m, a.Time("transaction_time") ?? after)).ToList();
            fills.AddRange(batch);
            if (batch.Count < 100)
            {
                break;
            }

            pageToken = batch[^1].Id;
        }

        return fills;
    }

    internal static AlpacaOrder ToOrder(JsonElement o) => new(
        o.String("id") ?? string.Empty,
        o.String("client_order_id") ?? string.Empty,
        o.String("status") ?? "unknown",
        o.String("symbol") ?? string.Empty,
        o.String("side") ?? "buy",
        o.String("type") ?? o.String("order_type") ?? "market",
        o.Decimal("qty") ?? 0m,
        o.Decimal("filled_qty") ?? 0m,
        o.Decimal("filled_avg_price"),
        o.Time("filled_at"),
        o.Decimal("stop_price"),
        o.Decimal("limit_price"),
        o.String("order_class") ?? "simple",
        o.TryGetProperty("legs", out var legs) && legs.ValueKind == JsonValueKind.Array ? legs.EnumerateArray().Select(ToOrder).ToList() : []);

    private static bool Bool(JsonElement element, string name) => element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static string Text(decimal value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
