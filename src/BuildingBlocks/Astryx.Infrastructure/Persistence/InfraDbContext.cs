using Astryx.Infrastructure.Idempotency;
using Astryx.Infrastructure.Jobs;
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

    public DbSet<JobRecord> JobQueue => Set<JobRecord>();

    public DbSet<RecurringJobState> RecurringJobs => Set<RecurringJobState>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

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

        modelBuilder.Entity<JobRecord>(entity =>
        {
            entity.ToTable("job_queue", t =>
            {
                t.HasCheckConstraint("ck_job_queue_status", "status IN ('pending', 'running', 'succeeded', 'dead')");
                t.HasCheckConstraint("ck_job_queue_attempts", "attempts >= 0 AND max_attempts >= 1 AND timeout_seconds >= 1");
            });
            entity.HasKey(j => j.Id);
            entity.Property(j => j.Type).HasMaxLength(200);
            entity.Property(j => j.Payload).HasColumnType("jsonb");
            entity.Property(j => j.Status).HasMaxLength(16);
            entity.Property(j => j.LockedBy).HasMaxLength(200);
            entity.Property(j => j.DedupeKey).HasMaxLength(300);
            entity.Property(j => j.CorrelationId).HasMaxLength(64);
            entity.Property(j => j.LastError).HasMaxLength(2000);
            entity.HasIndex(j => j.RunAfter).HasFilter("status = 'pending'").HasDatabaseName("ix_job_queue_due");
            entity.HasIndex(j => j.LockedUntil).HasFilter("status = 'running'").HasDatabaseName("ix_job_queue_leases");
            entity.HasIndex(j => j.DedupeKey).IsUnique().HasFilter("status IN ('pending', 'running')").HasDatabaseName("ux_job_queue_active_dedupe");
        });

        modelBuilder.Entity<RecurringJobState>(entity =>
        {
            entity.ToTable("recurring_jobs");
            entity.HasKey(r => r.Name);
            entity.Property(r => r.Name).HasMaxLength(200);
        });

        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.ToTable("idempotency_records", t => t.HasCheckConstraint("ck_idempotency_records_status", "status IN ('in_progress', 'completed')"));
            entity.HasKey(r => new { r.Subject, r.Key });
            entity.Property(r => r.Subject).HasMaxLength(64);
            entity.Property(r => r.Key).HasMaxLength(128);
            entity.Property(r => r.Endpoint).HasMaxLength(300);
            entity.Property(r => r.RequestHash).HasMaxLength(64);
            entity.Property(r => r.Status).HasMaxLength(16);
            entity.Property(r => r.ResponseContentType).HasMaxLength(200);
            entity.HasIndex(r => r.ExpiresAt).HasDatabaseName("ix_idempotency_records_expires_at");
        });
    }
}
