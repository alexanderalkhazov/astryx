using Astryx.Infrastructure.Jobs;
using Astryx.Modules.Trading.Domain;
using Astryx.Modules.Trading.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Astryx.Modules.Trading.Application;

/// <summary><c>ProposalExpiryJob</c> (SYSTEM_DESIGN.md §19.2): every minute, pending proposals past <c>expires_at</c> expire.</summary>
public sealed record ProposalExpiryJob : IJob
{
    public static string JobType => "trading.proposal_expiry.v1";
}

public sealed class ProposalExpiryHandler(TradingDbContext db, TimeProvider timeProvider, ILogger<ProposalExpiryHandler> logger) : IJobHandler<ProposalExpiryJob>
{
    public async Task HandleAsync(ProposalExpiryJob job, JobContext context, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var expired = await db.Proposals.IgnoreQueryFilters()
            .Where(p => p.Status == ProposalStatus.Pending && p.ExpiresAt <= now)
            .ExecuteUpdateAsync(
                s => s.SetProperty(p => p.Status, ProposalStatus.Expired).SetProperty(p => p.RejectReason, "expired")
                    .SetProperty(p => p.DecidedAt, now).SetProperty(p => p.Version, p => p.Version + 1),
                cancellationToken);
        if (expired > 0)
        {
            logger.LogInformation("Expired {Count} pending proposal(s)", expired);
        }
    }
}
