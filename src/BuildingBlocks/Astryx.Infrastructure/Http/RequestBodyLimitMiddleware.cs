using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Astryx.Infrastructure.Http;

/// <summary>Rejects request bodies over 64 KB with 413 (SYSTEM_DESIGN.md §18.7).</summary>
public sealed class RequestBodyLimitMiddleware(RequestDelegate next)
{
    public const long MaxBodyBytes = 64 * 1024;

    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.ContentLength > MaxBodyBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return Task.CompletedTask;
        }

        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false })
        {
            feature.MaxRequestBodySize = MaxBodyBytes;
        }

        return next(context);
    }
}
