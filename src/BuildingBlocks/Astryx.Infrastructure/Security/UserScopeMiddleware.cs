using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Astryx.Infrastructure.Security;

/// <summary>Runs after authentication: an authenticated request's <c>sub</c> becomes the data scope for the request.</summary>
public sealed class UserScopeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        Guid? userId = context.User.Identity?.IsAuthenticated == true && Guid.TryParse(context.User.FindFirst("sub")?.Value, out var id) ? id : null;
        UserScope.Set(userId);
        try
        {
            await next(context);
        }
        finally
        {
            UserScope.Set(null);
        }
    }
}

public static class UserScopeExtensions
{
    /// <summary>Call after <c>UseAuthentication()</c>.</summary>
    public static IApplicationBuilder UseUserScope(this IApplicationBuilder app) => app.UseMiddleware<UserScopeMiddleware>();
}
