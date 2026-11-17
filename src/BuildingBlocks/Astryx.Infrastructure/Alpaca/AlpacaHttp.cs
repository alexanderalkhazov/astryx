using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Astryx.Infrastructure.Alpaca;

/// <summary>Alpaca's two trading environments (SYSTEM_DESIGN.md §17.1). Paper and live use different keys and hosts.</summary>
public enum AlpacaEnvironment
{
    Paper,
    Live,
}

/// <summary>
/// An Alpaca API key pair. <see cref="ToString"/> never prints the secret, so a credential that reaches a log or an
/// exception message stays redacted.
/// </summary>
public sealed record AlpacaCredentials(string KeyId, string SecretKey)
{
    /// <summary>The last four characters of the key ID: the only part ever shown (Settings "API key ••••3f2a").</summary>
    public string KeySuffix => KeyId.Length <= 4 ? KeyId : KeyId[^4..];

    public override string ToString() => $"AlpacaCredentials {{ KeyId = ••••{KeySuffix} }}";
}

/// <summary><c>Alpaca</c> settings. The key pair is the operator's own, used for market data (SYSTEM_DESIGN.md §16.2).</summary>
public sealed class AlpacaOptions
{
    public const string SectionName = "Alpaca";

    public string? KeyId { get; set; }

    public string? SecretKey { get; set; }

    public string DataBaseUrl { get; set; } = "https://data.alpaca.markets";

    public string PaperBaseUrl { get; set; } = "https://paper-api.alpaca.markets";

    public string LiveBaseUrl { get; set; } = "https://api.alpaca.markets";

    /// <summary>Alpaca allows 200 requests a minute per account; stay a little under it.</summary>
    public int RequestsPerMinute { get; set; } = 180;

    public AlpacaCredentials? Credentials => string.IsNullOrWhiteSpace(KeyId) || string.IsNullOrWhiteSpace(SecretKey) ? null : new AlpacaCredentials(KeyId.Trim(), SecretKey.Trim());

    public Uri TradingBaseUrl(AlpacaEnvironment environment) => new((environment == AlpacaEnvironment.Paper ? PaperBaseUrl : LiveBaseUrl).TrimEnd('/') + "/");
}

/// <summary>How an Alpaca call failed, mapped to the app's error codes (SYSTEM_DESIGN.md §17.3, TASKS.md T-140).</summary>
public enum AlpacaFailure
{
    CredentialsInvalid,
    Forbidden,
    InsufficientFunds,
    NotFound,
    Invalid,
    RateLimited,
    Unavailable,
}

public sealed class AlpacaApiException(AlpacaFailure failure, int? status, string message) : Exception(message)
{
    public AlpacaFailure Failure { get; } = failure;

    public int? Status { get; } = status;
}

/// <summary>One token bucket per Alpaca key: the 200 requests/minute limit is per account, shared by every caller.</summary>
public sealed class AlpacaRateLimiter(IOptions<AlpacaOptions> options) : IDisposable
{
    private readonly ConcurrentDictionary<string, RateLimiter> _buckets = new(StringComparer.Ordinal);

    public async ValueTask WaitAsync(AlpacaCredentials credentials, CancellationToken cancellationToken)
    {
        var bucket = _buckets.GetOrAdd(credentials.KeyId, _ => new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = Math.Max(1, options.Value.RequestsPerMinute / 6),
            TokensPerPeriod = Math.Max(1, options.Value.RequestsPerMinute / 6),
            ReplenishmentPeriod = TimeSpan.FromSeconds(10),
            QueueLimit = 1_000,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        }));
        using var lease = await bucket.AcquireAsync(1, cancellationToken);
        if (!lease.IsAcquired)
        {
            throw new AlpacaApiException(AlpacaFailure.RateLimited, null, "Alpaca request queue is full.");
        }
    }

    public void Dispose()
    {
        foreach (var bucket in _buckets.Values)
        {
            bucket.Dispose();
        }
    }
}

/// <summary>
/// Sends one authenticated Alpaca request and parses the JSON answer. Keys go only in the <c>APCA-API-*</c> headers;
/// error messages carry Alpaca's message and status, never the request headers.
/// </summary>
public static class AlpacaHttp
{
    public static async Task<JsonDocument> SendAsync(
        HttpClient http,
        AlpacaRateLimiter limiter,
        AlpacaCredentials credentials,
        HttpMethod method,
        Uri uri,
        object? body,
        CancellationToken cancellationToken)
    {
        await limiter.WaitAsync(credentials, cancellationToken);
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Add("APCA-API-KEY-ID", credentials.KeyId);
        request.Headers.Add("APCA-API-SECRET-KEY", credentials.SecretKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, BodyJson), Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new AlpacaApiException(AlpacaFailure.Unavailable, null, $"Alpaca is unreachable: {ex.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AlpacaApiException(AlpacaFailure.Unavailable, null, "Alpaca timed out.");
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                try
                {
                    return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
                }
                catch (JsonException)
                {
                    throw new AlpacaApiException(AlpacaFailure.Unavailable, (int)response.StatusCode, "Alpaca returned a malformed response.");
                }
            }

            throw ToException(response.StatusCode, text);
        }
    }

    internal static AlpacaApiException ToException(HttpStatusCode status, string body)
    {
        var message = Message(body);
        var failure = status switch
        {
            HttpStatusCode.Unauthorized => AlpacaFailure.CredentialsInvalid,
            // Alpaca answers 403 both for a bad key ("forbidden") and for buying power or shares (§17.3).
            HttpStatusCode.Forbidden when message.Contains("buying power", StringComparison.OrdinalIgnoreCase)
                || message.Contains("insufficient", StringComparison.OrdinalIgnoreCase) => AlpacaFailure.InsufficientFunds,
            HttpStatusCode.Forbidden when message.Contains("subscription", StringComparison.OrdinalIgnoreCase) => AlpacaFailure.Forbidden,
            HttpStatusCode.Forbidden => AlpacaFailure.CredentialsInvalid,
            HttpStatusCode.NotFound => AlpacaFailure.NotFound,
            HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest => AlpacaFailure.Invalid,
            HttpStatusCode.TooManyRequests => AlpacaFailure.RateLimited,
            _ => AlpacaFailure.Unavailable,
        };
        return new AlpacaApiException(failure, (int)status, $"Alpaca {(int)status}: {message}");
    }

    private static string Message(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
            {
                return m.GetString()!;
            }
        }
        catch (JsonException)
        {
        }

        return body.Length > 200 ? body[..200] : body;
    }

    private static readonly JsonSerializerOptions BodyJson = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
}

/// <summary>Reading Alpaca JSON: prices arrive as JSON numbers or numeric strings and are always read as decimals.</summary>
public static class AlpacaJson
{
    public static decimal? Decimal(this JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(value.GetString(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        } : null;

    public static string? String(this JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public static DateTimeOffset? Time(this JsonElement element, string name) =>
        element.String(name) is { } text && DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;

    public static JsonElement? Object(this JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;
}

public static class AlpacaServiceCollectionExtensions
{
    /// <summary>Alpaca options, the shared per-key rate limiter and the <c>alpaca</c> HTTP client. Safe to call from every module.</summary>
    public static IServiceCollection AddAlpacaCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AlpacaOptions>(configuration.GetSection(AlpacaOptions.SectionName));
        services.TryAddSingleton<AlpacaRateLimiter>();
        services.AddHttpClient(AlpacaHttpClient.Name, client => client.Timeout = TimeSpan.FromSeconds(20));
        return services;
    }
}

public static class AlpacaHttpClient
{
    public const string Name = "alpaca";
}
