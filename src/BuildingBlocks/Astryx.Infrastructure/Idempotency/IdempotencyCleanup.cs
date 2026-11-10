using Astryx.Infrastructure.Jobs;
using Astryx.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Astryx.Infrastructure.Idempotency;

/// <summary>Deletes idempotency records past their retention window (SYSTEM_DESIGN.md §10.5).</summary>
public sealed record IdempotencyCleanupJob : IJob
{
    public static string JobType => "infra.idempotency_cleanup.v1";
}

public sealed class IdempotencyCleanupHandler(InfraDbContext db, TimeProvider timeProvider, ILogger<IdempotencyCleanupHandler> logger)
    : IJobHandler<IdempotencyCleanupJob>
{
    private const int BatchSize = 5_000;

    public async Task HandleAsync(IdempotencyCleanupJob job, JobContext context, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var total = 0;
        int deleted;
        do
        {
            deleted = await db.Database.ExecuteSqlAsync(
                $"""
                DELETE FROM infra.idempotency_records
                WHERE ctid IN (SELECT ctid FROM infra.idempotency_records WHERE expires_at < {now} LIMIT {BatchSize})
                """,
                cancellationToken);
            total += deleted;
        }
        while (deleted == BatchSize);

        logger.LogInformation("Deleted {Count} expired idempotency record(s)", total);
    }
}
