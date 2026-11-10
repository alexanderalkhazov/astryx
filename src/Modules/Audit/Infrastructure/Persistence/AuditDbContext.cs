using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Audit.Domain;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Audit.Infrastructure.Persistence;

/// <summary>Audit module data in schema <c>audit</c> (SYSTEM_DESIGN.md §10.4).</summary>
public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "audit";

    public override string Schema => SchemaName;

    public DbSet<AuditLogRecord> AuditLogs => Set<AuditLogRecord>();

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<AuditLogRecord>(entity =>
        {
            // Created by hand-written SQL in the AddAuditLogs migration: range-partitioned by month, append-only trigger.
            entity.ToTable("audit_logs", t => t.HasCheckConstraint("ck_audit_logs_actor_type", "actor_type IN ('user', 'system', 'worker', 'admin')"));
            entity.HasKey(a => new { a.Id, a.OccurredAt });
            entity.Property(a => a.ActorType).HasMaxLength(16);
            entity.Property(a => a.Action).HasMaxLength(100);
            entity.Property(a => a.EntityType).HasMaxLength(100);
            entity.Property(a => a.EntityId).HasMaxLength(100);
            entity.Property(a => a.CorrelationId).HasMaxLength(64);
            entity.Property(a => a.Ip).HasColumnType("inet");
            entity.Property(a => a.UserAgent).HasMaxLength(256);
            entity.Property(a => a.Before).HasColumnType("jsonb");
            entity.Property(a => a.After).HasColumnType("jsonb");
            entity.Property(a => a.Metadata).HasColumnType("jsonb");
            entity.HasIndex(a => new { a.UserId, a.OccurredAt }).IsDescending(false, true).HasDatabaseName("ix_audit_logs_user_occurred");
            entity.HasIndex(a => new { a.EntityType, a.EntityId }).HasDatabaseName("ix_audit_logs_entity");
        });
}
