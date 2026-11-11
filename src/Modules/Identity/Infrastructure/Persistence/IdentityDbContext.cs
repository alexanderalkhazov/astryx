using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Identity.Infrastructure.Persistence;

/// <summary>Identity module data in schema <c>identity</c> (SYSTEM_DESIGN.md §10.4).</summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "identity";

    public override string Schema => SchemaName;

    public DbSet<UserAccount> Users => Set<UserAccount>();

    public DbSet<UserToken> UserTokens => Set<UserToken>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("citext");

        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Email).HasColumnType("citext").HasMaxLength(254);
            entity.Property(u => u.NormalizedEmail).HasMaxLength(254);
            entity.Property(u => u.PasswordHash).HasMaxLength(512);
            entity.Property(u => u.SecurityStamp).HasMaxLength(64);
            entity.HasIndex(u => u.Email).IsUnique();
            entity.HasIndex(u => u.NormalizedEmail).IsUnique();
            entity.HasIndex(u => u.DeletedAt).HasFilter("deleted_at IS NOT NULL");
        });

        modelBuilder.Entity<UserToken>(entity =>
        {
            entity.ToTable("user_tokens", t => t.HasCheckConstraint("ck_user_tokens_purpose", "purpose IN ('email_verification', 'password_reset')"));
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Purpose).HasMaxLength(32);
            entity.HasIndex(t => t.TokenHash).IsUnique();
            entity.HasIndex(t => t.SourceMessageId).IsUnique().HasFilter("source_message_id IS NOT NULL");
            entity.HasIndex(t => new { t.UserId, t.Purpose }).HasFilter("consumed_at IS NULL");
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refresh_tokens", t => t.HasCheckConstraint(
                "ck_refresh_tokens_revoked_reason",
                "revoked_reason IS NULL OR revoked_reason IN ('rotated', 'logout', 'reuse_detected', 'password_changed', 'superseded', 'admin')"));
            entity.HasKey(t => t.Id);
            entity.Property(t => t.DeviceId).HasMaxLength(64);
            entity.Property(t => t.RevokedReason).HasMaxLength(32);
            entity.Property(t => t.CreatedIp).HasColumnType("inet");
            entity.Property(t => t.UserAgent).HasMaxLength(256);
            entity.HasIndex(t => t.TokenHash).IsUnique();
            entity.HasIndex(t => new { t.UserId, t.DeviceId });
            entity.HasIndex(t => t.FamilyId);
            entity.HasIndex(t => t.ExpiresAt);
            entity.HasOne<UserAccount>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<RefreshToken>().WithMany().HasForeignKey(t => t.ReplacedById).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
