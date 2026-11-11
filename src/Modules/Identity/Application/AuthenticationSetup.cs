using System.Security.Claims;
using Astryx.Infrastructure.Http;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Astryx.Modules.Identity.Application;

public static class AuthenticationSetup
{
    public const string AuthenticatedPolicy = "Authenticated";

    /// <summary>JWT bearer validation (signature, issuer, audience, lifetime, security stamp) and the default policy (§18.5).</summary>
    public static IServiceCollection AddAstryxAuthentication(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<SecurityStampCache>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<JwtKeyRing, IOptions<JwtOptions>>((bearer, keys, jwt) =>
            {
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Value.Issuer,
                    ValidAudience = jwt.Value.Audience,
                    IssuerSigningKeys = keys.ValidationKeys,
                    ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = AstryxClaims.Subject,
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                };
                bearer.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var principal = context.Principal!;
                        var stamps = context.HttpContext.RequestServices.GetRequiredService<SecurityStampCache>();
                        var valid = Guid.TryParse(principal.FindFirstValue(AstryxClaims.Subject), out var userId)
                            && principal.FindFirstValue(AstryxClaims.SecurityStamp) is { } fingerprint
                            && await stamps.IsCurrentAsync(userId, fingerprint, context.HttpContext.RequestAborted);
                        if (!valid)
                        {
                            context.HttpContext.Items[ChallengeCodeKey] = "session_revoked";
                            context.Fail("Security stamp is no longer current.");
                        }
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        var code = context.HttpContext.Items[ChallengeCodeKey] as string
                            ?? (context.AuthenticateFailure is SecurityTokenExpiredException ? "token_expired" : "unauthorized");
                        var detail = code switch
                        {
                            "token_expired" => "The access token has expired. Refresh it and retry.",
                            "session_revoked" => "This session was signed out. Sign in again.",
                            _ => "A valid access token is required.",
                        };
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.Headers.WWWAuthenticate = "Bearer";
                        await Results.Problem(statusCode: 401, detail: detail, type: ErrorHttpMapping.TypeUri(code),
                            extensions: new Dictionary<string, object?> { ["code"] = code }).ExecuteAsync(context.HttpContext);
                    },
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthenticatedPolicy, policy => policy.RequireAuthenticatedUser().RequireClaim(AstryxClaims.Subject));
        return services;
    }

    private const string ChallengeCodeKey = "astryx:auth:challenge-code";

    /// <summary>The authenticated user's id (the <c>sub</c> claim).</summary>
    public static Guid UserId(this ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(AstryxClaims.Subject)!);
}
