using System.Diagnostics;
using System.Text.Json.Serialization;
using Astryx.Infrastructure.Idempotency;
using Astryx.SharedKernel.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Infrastructure.Http;

/// <summary>Request pipeline shared by every endpoint (SYSTEM_DESIGN.md §9.2, §11.1).</summary>
public static class HttpPipeline
{
    public static IServiceCollection AddAstryxHttp(this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        services.AddAstryxRateLimiting(configuration);
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            options.SerializerOptions.AddFinancialConverters();
            options.SerializerOptions.Converters.Add(new UtcDateTimeOffsetJsonConverter());
        });

        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var problem = context.ProblemDetails;
            var status = problem.Status ?? context.HttpContext.Response.StatusCode;
            var code = problem.Extensions.TryGetValue("code", out var existing) && existing is string s ? s : ErrorHttpMapping.DefaultCodeFor(status);
            problem.Extensions["code"] = code;
            problem.Type = problem.Type is null or "" || problem.Type.StartsWith("https://tools.ietf.org", StringComparison.Ordinal)
                ? ErrorHttpMapping.TypeUri(code)
                : problem.Type;
            problem.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
            if (CorrelationIdMiddleware.Get(context.HttpContext) is { } correlationId)
            {
                problem.Extensions["correlationId"] = correlationId;
            }
        });

        return services;
    }

    /// <summary>
    /// Response headers for a JSON API used by a native app (SYSTEM_DESIGN.md §18.9, ASVS V14.4): no MIME sniffing, no
    /// framing, no referrer, and personal data never cached by intermediaries. TLS and HSTS terminate at the edge.
    /// </summary>
    private static Task SecurityHeaders(HttpContext context, Func<Task> next)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            headers.CacheControl = "no-store";
        }

        return next();
    }

    /// <summary>Correlation → security headers → exception handler → status-code problem details → body limit → idempotency buffering.</summary>
    public static WebApplication UseAstryxHttp(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.Use(SecurityHeaders);
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            StatusCodeSelector = exception => exception is BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status500InternalServerError,
        });
        app.UseStatusCodePages();
        app.UseMiddleware<RequestBodyLimitMiddleware>();
        app.UseMiddleware<IdempotencyBufferingMiddleware>();
        return app;
    }
}
