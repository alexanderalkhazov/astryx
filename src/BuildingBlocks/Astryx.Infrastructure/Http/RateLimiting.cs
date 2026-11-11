using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Infrastructure.Http;

/// <summary>Bound from configuration section <c>RateLimits</c> (SYSTEM_DESIGN.md §18.10).</summary>
public sealed class RateLimitOptions
{
    public int AuthPerMinute { get; set; } = 10;

    public int AuthPerHour { get; set; } = 50;

    public int UserPerMinute { get; set; } = 120;

    public int AnonymousPerMinute { get; set; } = 300;

    public int AssessmentPerMinute { get; set; } = 60;

    public int OrdersPerMinute { get; set; } = 10;

    public int AnalysisPerHour { get; set; } = 10;

    public int AnalysisPerDay { get; set; } = 30;

    public int SyncPer10Seconds { get; set; } = 1;
}

/// <summary>Named policies endpoints attach with <c>RequireRateLimiting(...)</c>.</summary>
public static class RateLimitPolicies
{
    /// <summary>Anonymous auth endpoints, per client IP. Per-account brute force is stopped by login lockout.</summary>
    public const string AuthStrict = "auth-strict";
    public const string Assessment = "assessment";
    public const string Orders = "orders";
    public const string Analysis = "analysis";
    public const string Sync = "sync";
}

public static class RateLimiting
{
    public static IServiceCollection AddAstryxRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var limits = configuration.GetSection("RateLimits").Get<RateLimitOptions>() ?? new RateLimitOptions();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? (int)Math.Ceiling(wait.TotalSeconds) : 60;
                context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
                await Results.Problem(
                        statusCode: StatusCodes.Status429TooManyRequests,
                        detail: $"Too many requests. Try again in {retryAfter} seconds.",
                        type: ErrorHttpMapping.TypeUri("rate_limited"),
                        extensions: new Dictionary<string, object?> { ["code"] = "rate_limited" })
                    .ExecuteAsync(context.HttpContext);
            };

            // Every request: per signed-in user, else per client IP. Health checks are never limited.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                if (http.Request.Path.StartsWithSegments("/health"))
                {
                    return RateLimitPartition.GetNoLimiter("health");
                }

                var userId = http.User.FindFirst("sub")?.Value;
                return userId is not null
                    ? Fixed($"user:{userId}", limits.UserPerMinute, TimeSpan.FromMinutes(1))
                    : Fixed($"ip:{ClientIp(http)}", limits.AnonymousPerMinute, TimeSpan.FromMinutes(1));
            });

            options.AddPolicy(RateLimitPolicies.AuthStrict, http => Chained(
                Fixed($"auth-min:{ClientIp(http)}", limits.AuthPerMinute, TimeSpan.FromMinutes(1)),
                Fixed($"auth-hour:{ClientIp(http)}", limits.AuthPerHour, TimeSpan.FromHours(1))));
            options.AddPolicy(RateLimitPolicies.Assessment, http => Fixed($"assess:{UserKey(http)}", limits.AssessmentPerMinute, TimeSpan.FromMinutes(1)));
            options.AddPolicy(RateLimitPolicies.Orders, http => Fixed($"orders:{UserKey(http)}", limits.OrdersPerMinute, TimeSpan.FromMinutes(1)));
            options.AddPolicy(RateLimitPolicies.Analysis, http => Chained(
                Fixed($"analysis-hour:{UserKey(http)}", limits.AnalysisPerHour, TimeSpan.FromHours(1)),
                Fixed($"analysis-day:{UserKey(http)}", limits.AnalysisPerDay, TimeSpan.FromDays(1))));
            options.AddPolicy(RateLimitPolicies.Sync, http => Fixed($"sync:{UserKey(http)}", limits.SyncPer10Seconds, TimeSpan.FromSeconds(10)));
        });
        return services;
    }

    private static RateLimitPartition<string> Fixed(string key, int permits, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = window,
            QueueLimit = 0,
            AutoReplenishment = true,
        });

    /// <summary>Both limits must grant a permit; the tighter one decides.</summary>
    private static RateLimitPartition<string> Chained(RateLimitPartition<string> first, RateLimitPartition<string> second) =>
        RateLimitPartition.Get($"{first.PartitionKey}|{second.PartitionKey}", _ =>
            new ChainedRateLimiter(first.Factory(first.PartitionKey), second.Factory(second.PartitionKey)));

    private static string ClientIp(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static string UserKey(HttpContext http) => http.User.FindFirst("sub")?.Value ?? $"ip:{ClientIp(http)}";

    /// <summary>Acquires from both limiters; releases the first if the second refuses, so a refusal costs nothing.</summary>
    private sealed class ChainedRateLimiter(RateLimiter first, RateLimiter second) : RateLimiter
    {
        public override TimeSpan? IdleDuration => first.IdleDuration is { } a && second.IdleDuration is { } b ? (a < b ? a : b) : null;

        public override RateLimiterStatistics? GetStatistics() => first.GetStatistics();

        protected override RateLimitLease AttemptAcquireCore(int permitCount)
        {
            var a = first.AttemptAcquire(permitCount);
            if (!a.IsAcquired)
            {
                return a;
            }

            var b = second.AttemptAcquire(permitCount);
            if (b.IsAcquired)
            {
                return new CombinedLease(a, b);
            }

            a.Dispose();
            return b;
        }

        protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
        {
            var a = await first.AcquireAsync(permitCount, cancellationToken);
            if (!a.IsAcquired)
            {
                return a;
            }

            var b = await second.AcquireAsync(permitCount, cancellationToken);
            if (b.IsAcquired)
            {
                return new CombinedLease(a, b);
            }

            a.Dispose();
            return b;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                first.Dispose();
                second.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class CombinedLease(RateLimitLease a, RateLimitLease b) : RateLimitLease
    {
        public override bool IsAcquired => true;

        public override IEnumerable<string> MetadataNames => a.MetadataNames.Union(b.MetadataNames);

        public override bool TryGetMetadata(string metadataName, out object? metadata) =>
            a.TryGetMetadata(metadataName, out metadata) || b.TryGetMetadata(metadataName, out metadata);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                a.Dispose();
                b.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
