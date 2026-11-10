using Astryx.Contracts.Audit;
using Astryx.Infrastructure.Audit;
using Astryx.Modules.Audit.Infrastructure.Persistence;

namespace Astryx.Modules.Audit.Application;

/// <summary>Standalone audit writes, committed immediately on their own connection.</summary>
public sealed class AuditLogWriter(AuditDbContext db, TimeProvider timeProvider) : IAuditLog
{
    public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) =>
        AuditWriter.InsertAsync(db.Database, entry, timeProvider.GetUtcNow(), cancellationToken);
}
