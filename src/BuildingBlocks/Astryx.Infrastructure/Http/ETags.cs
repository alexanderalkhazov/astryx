using Microsoft.AspNetCore.Http;

namespace Astryx.Infrastructure.Http;

/// <summary>Version ETags <c>"v{version}"</c> for concurrency-controlled resources (SYSTEM_DESIGN.md §11.1).</summary>
public static class ETags
{
    public static string Format(int version) => $"\"v{version}\"";

    /// <summary>The version in <c>If-Match</c>; null when the header is missing, -1 (never matches) when it can't be parsed.</summary>
    public static int? ParseIfMatch(HttpContext http)
    {
        var header = http.Request.Headers.IfMatch.ToString().Trim();
        if (header.StartsWith("W/", StringComparison.Ordinal))
        {
            header = header[2..];
        }

        return header.Length > 3 && header.StartsWith("\"v", StringComparison.Ordinal) && header.EndsWith('"') && int.TryParse(header[2..^1], out var version)
            ? version
            : header.Length == 0 ? null : -1; // present but unparseable never matches
    }
}
