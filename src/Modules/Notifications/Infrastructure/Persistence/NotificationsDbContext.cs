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

    public DbSet<PushDevice> PushDevices => Set<PushDevice>();

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

        modelBuilder.Entity<PushDevice>(entity =>
        {
            entity.ToTable("push_devices", t => t.HasCheckConstraint("ck_push_devices_platform", "platform IN ('ios', 'android')"));
            entity.HasKey(d => d.Id);
            entity.Property(d => d.DeviceId).HasMaxLength(128);
            entity.Property(d => d.PushToken).HasMaxLength(256);
            entity.Property(d => d.Platform).HasMaxLength(8);
            entity.Property(d => d.RevokedReason).HasMaxLength(40);
            entity.HasIndex(d => d.PushToken).IsUnique();
            entity.HasIndex(d => new { d.UserId, d.DeviceId }).IsUnique();
        });
    }
}
