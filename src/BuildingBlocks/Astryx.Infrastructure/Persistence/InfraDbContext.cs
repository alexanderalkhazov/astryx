using Astryx.Infrastructure.Outbox;
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

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<OutboxDelivery> OutboxDeliveries => Set<OutboxDelivery>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("citext");
        modelBuilder.HasPostgresExtension("pg_trgm");

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages");
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Type).HasMaxLength(200);
            entity.Property(m => m.Payload).HasColumnType("jsonb");
            entity.Property(m => m.CorrelationId).HasMaxLength(64);
            entity.Property(m => m.LastError).HasMaxLength(2000);
            entity.HasIndex(m => m.NextAttemptAt).HasFilter("processed_at IS NULL AND failed_at IS NULL").HasDatabaseName("ix_outbox_messages_pending");
            entity.HasIndex(m => m.ProcessedAt).HasFilter("processed_at IS NOT NULL").HasDatabaseName("ix_outbox_messages_processed");
        });

        modelBuilder.Entity<OutboxDelivery>(entity =>
        {
            entity.ToTable("outbox_deliveries");
            entity.HasKey(d => new { d.MessageId, d.Handler });
            entity.Property(d => d.Handler).HasMaxLength(300);
            entity.HasOne<OutboxMessage>().WithMany().HasForeignKey(d => d.MessageId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
