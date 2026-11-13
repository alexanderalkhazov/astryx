using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Notifications.Infrastructure.Persistence;

/// <summary>Notifications module data in schema <c>notify</c> (SYSTEM_DESIGN.md §10.4).</summary>
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "notify";

    public override string Schema => SchemaName;

    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("notifications", t =>
            {
                t.HasCheckConstraint("ck_notifications_category", "category IN ('orders', 'alerts', 'strategy', 'system')");
                t.HasCheckConstraint("ck_notifications_severity", "severity IN ('info', 'success', 'warning', 'error')");
            });
            entity.HasKey(n => n.Id);
            entity.Property(n => n.Category).HasMaxLength(16);
            entity.Property(n => n.Type).HasMaxLength(40);
            entity.Property(n => n.Title).HasMaxLength(200);
            entity.Property(n => n.Body).HasMaxLength(1000);
            entity.Property(n => n.Severity).HasMaxLength(8);
            entity.Property(n => n.DeepLink).HasMaxLength(300);
            entity.Property(n => n.EntityType).HasMaxLength(40);
            entity.Property(n => n.DedupeKey).HasMaxLength(200);
            entity.HasIndex(n => new { n.UserId, n.DedupeKey }).IsUnique();
            entity.HasIndex(n => new { n.UserId, n.CreatedAt }).IsDescending(false, true);
            entity.HasIndex(n => n.UserId).HasFilter("read_at IS NULL").HasDatabaseName("ix_notifications_unread");
        });
    }
}
