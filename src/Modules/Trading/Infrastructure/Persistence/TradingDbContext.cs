using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Trading.Domain;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Trading.Infrastructure.Persistence;

/// <summary>Trading module data in schema <c>trading</c> (SYSTEM_DESIGN.md §10.4).</summary>
public sealed class TradingDbContext(DbContextOptions<TradingDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "trading";

    public override string Schema => SchemaName;

    public DbSet<Proposal> Proposals => Set<Proposal>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Proposal>(entity =>
        {
            entity.ToTable("proposals", t =>
            {
                t.HasCheckConstraint("ck_proposals_source", "(source = 'signal' AND signal_id IS NOT NULL) OR (source = 'analysis' AND analysis_result_id IS NOT NULL)");
                t.HasCheckConstraint("ck_proposals_side", "side = 'buy'");
                t.HasCheckConstraint("ck_proposals_order_type", "order_type IN ('buy_stop', 'limit', 'market')");
                t.HasCheckConstraint("ck_proposals_tif", "time_in_force = 'day'");
                t.HasCheckConstraint("ck_proposals_levels", "stop_price < entry_price AND entry_price < target_price");
                t.HasCheckConstraint("ck_proposals_copy_source", "copy_source IN ('template', 'ai')");
                t.HasCheckConstraint("ck_proposals_status", "status IN ('pending', 'approved', 'rejected', 'expired')");
                t.HasCheckConstraint("ck_proposals_reject_reason", "reject_reason IS NULL OR reject_reason IN ('user_rejected', 'expired', 'superseded')");
            });
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Symbol).HasMaxLength(12);
            entity.Property(p => p.Source).HasMaxLength(10);
            entity.Property(p => p.StrategyName).HasMaxLength(80);
            entity.Property(p => p.Side).HasMaxLength(4);
            entity.Property(p => p.OrderType).HasMaxLength(10);
            entity.Property(p => p.TimeInForce).HasMaxLength(4);
            entity.Property(p => p.EntryPrice).HasPrecision(18, 6);
            entity.Property(p => p.StopPrice).HasPrecision(18, 6);
            entity.Property(p => p.TargetPrice).HasPrecision(18, 6);
            entity.Property(p => p.Headline).HasMaxLength(200);
            entity.Property(p => p.Summary).HasMaxLength(2000);
            entity.Property(p => p.CopySource).HasMaxLength(10);
            entity.Property(p => p.Evidence).HasColumnType("jsonb");
            entity.Property(p => p.Invalidation).HasColumnType("jsonb");
            entity.Property(p => p.Score).HasPrecision(5, 2);
            entity.Property(p => p.Status).HasMaxLength(10);
            entity.Property(p => p.RejectReason).HasMaxLength(16);
            entity.Property(p => p.RejectNote).HasMaxLength(200);
            entity.Property(p => p.Version).IsConcurrencyToken();
            entity.HasIndex(p => new { p.UserId, p.InstrumentId }).IsUnique().HasFilter("status = 'pending'").HasDatabaseName("ux_proposals_one_pending");
            entity.HasIndex(p => new { p.UserId, p.Status, p.CreatedAt }).IsDescending(false, false, true);
            entity.HasIndex(p => p.ExpiresAt).HasFilter("status = 'pending'").HasDatabaseName("ix_proposals_expiry");
        });
    }
}
