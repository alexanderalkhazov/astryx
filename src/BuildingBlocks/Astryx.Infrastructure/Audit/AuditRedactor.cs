using System.Text.Json;
using System.Text.Json.Nodes;

namespace Astryx.Infrastructure.Audit;

/// <summary>
/// Removes secrets from audit payloads before they are stored (SYSTEM_DESIGN.md §18.15). Matching ignores case,
/// underscores and hyphens, so <c>refresh_token</c>, <c>RefreshToken</c> and <c>refresh-token</c> are all caught.
/// </summary>
public static class AuditRedactor
{
    public const string Redacted = "[REDACTED]";
    private const int MaxPayloadChars = 16_384;

    private static readonly HashSet<string> SecretKeys = new(StringComparer.Ordinal)
    {
        "password", "passwordhash", "newpassword", "currentpassword", "token", "accesstoken", "refreshtoken",
        "idtoken", "secret", "clientsecret", "apikey", "userkey", "xapikey", "xuserkey", "authorization",
        "credentials", "credentialsciphertext", "privatekey", "signingkeys", "securitystamp", "otp",
        "secretkey", "keyid", "apcaapisecretkey", "apcaapikeyid", // Alpaca API credentials and their headers (§17.6)
    };

    private static readonly HashSet<string> EmailKeys = new(StringComparer.Ordinal) { "email", "normalizedemail" };

    /// <summary>Serialises <paramref name="value"/> and returns redacted JSON, or null for null input.</summary>
    public static string? ToRedactedJson(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var node = JsonSerializer.SerializeToNode(value, InfrastructureJson.Options);
        Redact(node);
        var json = node?.ToJsonString() ?? "null";
        return json.Length <= MaxPayloadChars
            ? json
            : JsonSerializer.Serialize(new { truncated = true, length = json.Length }, InfrastructureJson.Options);
    }

    private static void Redact(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, child) in obj.ToList())
                {
                    var normalised = Normalise(name);
                    if (SecretKeys.Contains(normalised))
                    {
                        obj[name] = Redacted;
                    }
                    else if (EmailKeys.Contains(normalised) && child is JsonValue v && v.TryGetValue<string>(out var email))
                    {
                        obj[name] = MaskEmail(email);
                    }
                    else
                    {
                        Redact(child);
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    Redact(item);
                }

                break;
        }
    }

    internal static string MaskEmail(string email)
    {
        var at = email.IndexOf('@', StringComparison.Ordinal);
        return at <= 0 ? Redacted : $"{email[0]}***{email[at..]}";
    }

    private static string Normalise(string name) =>
        string.Concat(name.Where(c => c != '_' && c != '-')).ToLowerInvariant();
}
