using Astryx.Infrastructure.Jobs;
using Astryx.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Astryx.Modules.Identity.Application;

/// <summary>Deletes refresh and verification/reset tokens 7 days after they expired or were revoked/used (SYSTEM_DESIGN.md §10.5).</summary>
public sealed record IdentityTokenCleanupJob : IJob
{
    public static string JobType => "identity.token_cleanup.v1";
}

public sealed class IdentityTokenCleanupHandler(IdentityDbContext db, TimeProvider timeProvider, ILogger<IdentityTokenCleanupHandler> logger)
    : IJobHandler<IdentityTokenCleanupJob>
{
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(7);

    public async Task HandleAsync(IdentityTokenCleanupJob job, JobContext context, CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow() - KeepFor;
        // Clear replacement links first so tokens can be deleted in any order.
        await db.RefreshTokens.Where(t => t.ReplacedById != null && db.RefreshTokens.Any(r => r.Id == t.ReplacedById && (r.ExpiresAt < cutoff || r.RevokedAt < cutoff)))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ReplacedById, (Guid?)null), cancellationToken);
        var refresh = await db.RefreshTokens.Where(t => t.ExpiresAt < cutoff || t.RevokedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
        var single = await db.UserTokens.Where(t => t.ExpiresAt < cutoff || t.ConsumedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
        logger.LogInformation("Deleted {Refresh} refresh token(s) and {Single} single-use token(s)", refresh, single);
    }
}
