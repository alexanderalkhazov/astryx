using Astryx.Infrastructure.Jobs;
using Astryx.Modules.Audit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Astryx.Modules.Audit.Application;

/// <summary>
/// Keeps monthly partitions of <c>audit.audit_logs</c> ahead of time and drops those past retention
/// (SYSTEM_DESIGN.md §10.5: 2 years). Runs daily; every step is idempotent.
/// </summary>
public sealed record AuditPartitionMaintenanceJob : IJob
{
    public static string JobType => "audit.partition_maintenance.v1";
}

public sealed class AuditPartitionMaintenanceHandler(AuditDbContext db, TimeProvider timeProvider, ILogger<AuditPartitionMaintenanceHandler> logger)
    : IJobHandler<AuditPartitionMaintenanceJob>
{
    public const int MonthsAhead = 2;
    public const int RetentionMonths = 24;

    public async Task HandleAsync(AuditPartitionMaintenanceJob job, JobContext context, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var month = new DateOnly(now.Year, now.Month, 1);
        for (var i = 0; i <= MonthsAhead; i++)
        {
            var monthStart = month.AddMonths(i);
            await db.Database.ExecuteSqlAsync($"SELECT audit.ensure_month_partition({monthStart})", cancellationToken);
        }

        var cutoff = month.AddMonths(-RetentionMonths);
        var dropped = await db.Database.SqlQuery<int>($"SELECT audit.drop_partitions_before({cutoff}) AS \"Value\"").SingleAsync(cancellationToken);
        if (dropped > 0)
        {
            logger.LogInformation("Dropped {Count} audit partition(s) older than {Cutoff}", dropped, cutoff);
        }
    }

    public static string PartitionName(DateOnly monthStart) => $"audit_logs_y{monthStart:yyyy}m{monthStart:MM}";
}
