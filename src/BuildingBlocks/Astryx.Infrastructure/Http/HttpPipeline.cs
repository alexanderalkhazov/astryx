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

    /// <summary>Correlation → exception handler → status-code problem details → body limit → idempotency buffering.</summary>
    public static WebApplication UseAstryxHttp(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
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
