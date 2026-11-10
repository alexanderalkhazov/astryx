using Microsoft.EntityFrameworkCore;

namespace Astryx.Infrastructure.Persistence;

/// <summary>
/// Platform schema <c>infra</c>: outbox, job queue and idempotency records (T-014–T-016), plus the Postgres extensions
/// the modules rely on. Migrated first.
/// </summary>
public sealed class InfraDbContext(DbContextOptions<InfraDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "infra";

    public override string Schema => SchemaName;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("citext");
        modelBuilder.HasPostgresExtension("pg_trgm");
    }
}
