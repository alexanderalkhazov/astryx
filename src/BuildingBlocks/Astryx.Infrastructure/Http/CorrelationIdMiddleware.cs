using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Astryx.Infrastructure.Http;

/// <summary>
/// Accepts a well-formed <c>X-Correlation-Id</c> or generates one, echoes it on the response, and adds it to the
/// log scope and the current trace (SYSTEM_DESIGN.md §23.2).
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        var correlationId = ValidId().IsMatch(incoming) ? incoming : Guid.NewGuid().ToString("N");

        context.Items[ItemKey] = correlationId;
        Activity.Current?.SetTag("correlation.id", correlationId);
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }

    public static string? Get(HttpContext context) => context.Items.TryGetValue(ItemKey, out var value) ? value as string : null;

    [GeneratedRegex("^[A-Za-z0-9._-]{8,64}$")]
    private static partial Regex ValidId();
}
