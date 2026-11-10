using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Astryx.Infrastructure.Http;
using Astryx.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astryx.Infrastructure.Idempotency;

/// <summary>Endpoint metadata marking a route as idempotent; the buffering middleware and the filter look for it.</summary>
public sealed class IdempotentEndpointMetadata;

/// <summary>
/// Makes a mutating endpoint safe to retry (SYSTEM_DESIGN.md §11.1, §20.10):
/// <list type="bullet">
/// <item>Missing or malformed <c>Idempotency-Key</c> → 400 <c>idempotency_key_required</c> / <c>idempotency_key_invalid</c>.</item>
/// <item>First request with a key claims it, runs, and stores the response (except 5xx).</item>
/// <item>Same key and body after completion → the stored response, with <c>Idempotent-Replayed: true</c>.</item>
/// <item>Same key while the first is running → 409 <c>request_in_progress</c>.</item>
/// <item>Same key with a different request → 422 <c>idempotency_key_reused</c>.</item>
/// </list>
/// </summary>
public sealed partial class IdempotencyFilter(
    IServiceProvider services,
    IOptions<IdempotencyOptions> options,
    TimeProvider timeProvider,
    ILogger<IdempotencyFilter> logger) : IEndpointFilter
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var key = http.Request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(key))
        {
            return Problem("idempotency_key_required", StatusCodes.Status400BadRequest, $"This endpoint requires an {HeaderName} header.");
        }

        if (!ValidKey().IsMatch(key))
        {
            return Problem("idempotency_key_invalid", StatusCodes.Status400BadRequest, $"{HeaderName} must be 8–128 characters of letters, digits, '-' or '_'.");
        }

        var subject = http.User.FindFirstValue("sub") ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
        var endpoint = $"{http.Request.Method} {http.Request.Path}";
        var requestHash = await HashRequestAsync(http);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InfraDbContext>();

        var claim = await ClaimAsync(db, subject, key, endpoint, requestHash, http.RequestAborted);
        if (claim is not null)
        {
            return claim;
        }

        return await ExecuteAndStoreAsync(context, next, db, subject, key);
    }

    /// <summary>Returns null when this request now owns the key; otherwise the response to send instead.</summary>
    private async Task<IResult?> ClaimAsync(InfraDbContext db, string subject, string key, string endpoint, string requestHash, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();
        var leaseUntil = now + settings.InProgressLease;
        var expiresAt = now + settings.Retention;

        // Expired records no longer protect anything; clear one so the key can be reused.
        await db.Database.ExecuteSqlAsync(
            $"DELETE FROM infra.idempotency_records WHERE subject = {subject} AND key = {key} AND expires_at < {now}",
            cancellationToken);

        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO infra.idempotency_records (subject, key, endpoint, request_hash, status, in_progress_until, created_at, expires_at)
            VALUES ({subject}, {key}, {endpoint}, {requestHash}, 'in_progress', {leaseUntil}, {now}, {expiresAt})
            ON CONFLICT (subject, key) DO NOTHING
            """,
            cancellationToken);
        if (inserted == 1)
        {
            return null;
        }

        var existing = await db.IdempotencyRecords.AsNoTracking().SingleAsync(r => r.Subject == subject && r.Key == key, cancellationToken);
        if (existing.RequestHash != requestHash || existing.Endpoint != endpoint)
        {
            return Problem("idempotency_key_reused", StatusCodes.Status422UnprocessableEntity,
                $"This {HeaderName} was already used for a different request. Generate a new key for a new request.");
        }

        if (existing.Status == IdempotencyStatus.Completed)
        {
            return new ReplayResult(existing);
        }

        // In progress: take over only if the original request was abandoned (its lease ran out).
        var takenOver = await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE infra.idempotency_records SET in_progress_until = {leaseUntil}
            WHERE subject = {subject} AND key = {key} AND status = 'in_progress' AND in_progress_until < {now}
            """,
            cancellationToken);
        if (takenOver == 1)
        {
            logger.LogWarning("Took over abandoned idempotent request {Endpoint} for key {Key}", endpoint, key);
            return null;
        }

        return Problem("request_in_progress", StatusCodes.Status409Conflict,
            "A request with this key is still being processed. Retry shortly with the same key.");
    }

    private async Task<object?> ExecuteAndStoreAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next, InfraDbContext db, string subject, string key)
    {
        var http = context.HttpContext;
        var originalBody = http.Response.Body;
        await using var buffer = new MemoryStream();
        http.Response.Body = buffer;
        try
        {
            var result = await next(context);
            switch (result)
            {
                case IResult endpointResult:
                    await endpointResult.ExecuteAsync(http);
                    break;
                case null:
                    break;
                default:
                    await Results.Ok(result).ExecuteAsync(http);
                    break;
            }
        }
        catch
        {
            http.Response.Body = originalBody;
            await ReleaseAsync(db, subject, key);
            throw;
        }

        http.Response.Body = originalBody;
        var status = http.Response.StatusCode;
        var body = buffer.ToArray();

        if (status >= 500)
        {
            // Server errors are not stored: the client may retry the same key and have the request run again.
            await ReleaseAsync(db, subject, key);
        }
        else
        {
            var text = Encoding.UTF8.GetString(body);
            var contentType = http.Response.ContentType;
            await db.Database.ExecuteSqlAsync(
                $"""
                UPDATE infra.idempotency_records
                SET status = 'completed', response_status = {status}, response_content_type = {contentType}, response_body = {text}
                WHERE subject = {subject} AND key = {key}
                """,
                CancellationToken.None);
        }

        await originalBody.WriteAsync(body, http.RequestAborted);
        return Results.Empty;
    }

    private static Task ReleaseAsync(InfraDbContext db, string subject, string key) =>
        db.Database.ExecuteSqlAsync(
            $"DELETE FROM infra.idempotency_records WHERE subject = {subject} AND key = {key} AND status = 'in_progress'",
            CancellationToken.None);

    private static async Task<string> HashRequestAsync(HttpContext http)
    {
        string body;
        if (http.Request.Body.CanSeek)
        {
            http.Request.Body.Position = 0;
            using var reader = new StreamReader(http.Request.Body, Encoding.UTF8, leaveOpen: true);
            body = await reader.ReadToEndAsync(http.RequestAborted);
            http.Request.Body.Position = 0;
        }
        else
        {
            body = string.Empty;
        }

        var material = $"{http.Request.Method}\n{http.Request.Path}{http.Request.QueryString}\n{body}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private static IResult Problem(string code, int status, string detail) =>
        Results.Problem(statusCode: status, detail: detail, type: ErrorHttpMapping.TypeUri(code), extensions: new Dictionary<string, object?> { ["code"] = code });

    [GeneratedRegex("^[A-Za-z0-9_-]{8,128}$")]
    private static partial Regex ValidKey();

    /// <summary>Writes a stored response back verbatim and marks it as a replay.</summary>
    private sealed class ReplayResult(IdempotencyRecord record) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = record.ResponseStatus ?? StatusCodes.Status200OK;
            httpContext.Response.Headers[ReplayedHeader] = "true";
            if (record.ResponseContentType is not null)
            {
                httpContext.Response.ContentType = record.ResponseContentType;
            }

            if (!string.IsNullOrEmpty(record.ResponseBody))
            {
                await httpContext.Response.WriteAsync(record.ResponseBody, httpContext.RequestAborted);
            }
        }
    }
}

/// <summary>Buffers request bodies of idempotent endpoints so the filter can hash the raw bytes after model binding.</summary>
public sealed class IdempotencyBufferingMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<IdempotentEndpointMetadata>() is not null)
        {
            context.Request.EnableBuffering();
        }

        return next(context);
    }
}

public static class IdempotencyEndpointExtensions
{
    /// <summary>Requires an <c>Idempotency-Key</c> and replays the first response for retries (SYSTEM_DESIGN.md §11.1).</summary>
    public static RouteHandlerBuilder RequireIdempotency(this RouteHandlerBuilder builder) =>
        builder
            .WithMetadata(new IdempotentEndpointMetadata())
            .AddEndpointFilter<IdempotencyFilter>()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
}
