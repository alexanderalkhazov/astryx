using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Astryx.Infrastructure.Alpaca;
using Microsoft.Extensions.Options;

namespace Astryx.Modules.Paper.Infrastructure.Alpaca;

/// <summary>One <c>trade_updates</c> event: <paramref name="Event"/> is Alpaca's (<c>new</c>, <c>fill</c>, <c>partial_fill</c>, <c>canceled</c>, …).</summary>
public sealed record AlpacaTradeUpdate(string Event, string OrderId, string? ClientOrderId, DateTimeOffset? At);

/// <summary>Alpaca's account stream, <c>trade_updates</c> only (SYSTEM_DESIGN.md §17.3, TASKS.md T-148).</summary>
public interface IAlpacaTradeUpdates
{
    /// <summary>
    /// Authenticates, listens and yields events until the socket closes (the sequence then ends) or fails (it throws).
    /// Rejected keys throw <see cref="AlpacaApiException"/> with <see cref="AlpacaFailure.CredentialsInvalid"/>.
    /// </summary>
    IAsyncEnumerable<AlpacaTradeUpdate> ListenAsync(AlpacaCredentials credentials, CancellationToken cancellationToken);
}

public sealed class AlpacaTradeUpdatesStream(IOptions<AlpacaOptions> options) : IAlpacaTradeUpdates
{
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(15L);

    /// <summary><c>wss://paper-api.alpaca.markets/stream</c> for the paper host (§17.3).</summary>
    public static Uri StreamUrl(Uri tradingBase) => new UriBuilder(tradingBase) { Scheme = tradingBase.Scheme == "http" ? "ws" : "wss", Path = "/stream", Port = -1 }.Uri;

    public async IAsyncEnumerable<AlpacaTradeUpdate> ListenAsync(AlpacaCredentials credentials, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20L);
        using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            handshake.CancelAfter(HandshakeTimeout);
            await socket.ConnectAsync(StreamUrl(options.Value.TradingBaseUrl(AlpacaEnvironment.Paper)), handshake.Token);
            await SendAsync(socket, new { action = "auth", key = credentials.KeyId, secret = credentials.SecretKey }, handshake.Token);
            if (await ReceiveAsync(socket, handshake.Token) is not { } reply || !IsAuthorized(reply))
            {
                throw new AlpacaApiException(AlpacaFailure.CredentialsInvalid, 401, "Alpaca stream: unauthorized.");
            }

            await SendAsync(socket, new { action = "listen", data = new { streams = new[] { "trade_updates" } } }, handshake.Token);
        }

        while (await ReceiveAsync(socket, cancellationToken) is { } message)
        {
            if (Parse(message) is { } update)
            {
                yield return update;
            }
        }
    }

    /// <summary>The authorization reply: <c>{"stream":"authorization","data":{"status":"authorized",…}}</c>.</summary>
    public static bool IsAuthorized(byte[] message)
    {
        using var json = JsonDocument.Parse(message);
        return json.RootElement.String("stream") == "authorization" && json.RootElement.TryGetProperty("data", out var data) && data.String("status") == "authorized";
    }

    /// <summary>A <c>trade_updates</c> message, or null for anything else (the <c>listening</c> acknowledgement, unknown streams).</summary>
    public static AlpacaTradeUpdate? Parse(byte[] message)
    {
        using var json = JsonDocument.Parse(message);
        var root = json.RootElement;
        if (root.String("stream") != "trade_updates" || !root.TryGetProperty("data", out var data) || data.String("event") is not { } @event
            || !data.TryGetProperty("order", out var order) || order.String("id") is not { } orderId)
        {
            return null;
        }

        return new AlpacaTradeUpdate(@event, orderId, order.String("client_order_id"), data.Time("timestamp"));
    }

    private static Task SendAsync(ClientWebSocket socket, object payload, CancellationToken cancellationToken) =>
        socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(payload), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);

    /// <summary>One whole message (Alpaca sends paper frames as binary), or null when the socket closed.</summary>
    private static async Task<byte[]?> ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var message = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return message.ToArray();
            }
        }
    }
}
