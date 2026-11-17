using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Paper.Domain;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Paper.Infrastructure.Persistence;

/// <summary>Paper module data in schema <c>trading</c> (SYSTEM_DESIGN.md §10.4): orders, fills and broker connections.</summary>
public sealed class PaperDbContext(DbContextOptions<PaperDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "trading";

    public override string Schema => SchemaName;

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<Fill> Fills => Set<Fill>();

    public DbSet<BrokerConnection> BrokerConnections => Set<BrokerConnection>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("orders", t =>
            {
                t.HasCheckConstraint("ck_orders_broker", "broker IN ('mock', 'alpaca')");
                t.HasCheckConstraint("ck_orders_mode", "mode IN ('simulated', 'alpaca_paper')");
                t.HasCheckConstraint("ck_orders_purpose", "purpose IN ('entry', 'exit_stop', 'exit_target', 'exit_manual', 'exit_reset')");
                t.HasCheckConstraint("ck_orders_side", "side IN ('buy', 'sell')");
                t.HasCheckConstraint("ck_orders_type", "order_type IN ('market', 'limit', 'buy_stop', 'sell_stop')");
                t.HasCheckConstraint("ck_orders_tif", "time_in_force IN ('day', 'gtc')");
                t.HasCheckConstraint("ck_orders_quantity", "quantity > 0");
                t.HasCheckConstraint("ck_orders_status", "status IN ('accepted', 'submitting', 'submitted', 'working', 'filled', 'cancelled', 'expired', 'rejected')");
                t.HasCheckConstraint("ck_orders_entry_assessed", "purpose <> 'entry' OR (proposal_id IS NOT NULL AND assessment_id IS NOT NULL)");
            });
            entity.HasKey(o => o.Id);
            entity.Property(o => o.Symbol).HasMaxLength(12);
            entity.Property(o => o.ClientOrderId).HasMaxLength(200);
            entity.Property(o => o.Broker).HasMaxLength(8);
            entity.Property(o => o.BrokerOrderId).HasMaxLength(64);
            entity.Property(o => o.Mode).HasMaxLength(16);
            entity.Property(o => o.Purpose).HasMaxLength(16);
            entity.Property(o => o.Side).HasMaxLength(4);
            entity.Property(o => o.OrderType).HasMaxLength(10);
            entity.Property(o => o.TimeInForce).HasMaxLength(4);
            entity.Property(o => o.Status).HasMaxLength(12);
            entity.Property(o => o.RejectCode).HasMaxLength(64);
            foreach (var price in new[] { nameof(Order.Quantity), nameof(Order.StopPrice), nameof(Order.LimitPrice), nameof(Order.FilledQuantity), nameof(Order.AvgFillPrice) })
            {
                entity.Property(price).HasPrecision(18, 6);
            }

            entity.Property(o => o.Version).IsConcurrencyToken();
            entity.HasIndex(o => new { o.UserId, o.ClientOrderId }).IsUnique();
            entity.HasIndex(o => new { o.Broker, o.BrokerOrderId }).IsUnique();
            entity.HasIndex(o => o.ProposalId).IsUnique().HasFilter("purpose = 'entry'").HasDatabaseName("ux_orders_one_entry_per_proposal");
            entity.HasIndex(o => new { o.PositionId, o.Purpose }).IsUnique().HasFilter("status IN ('accepted', 'working') AND purpose LIKE 'exit_%'").HasDatabaseName("ux_orders_one_live_exit");
            entity.HasIndex(o => o.Status).HasFilter("status = 'working'").HasDatabaseName("ix_orders_working");
            entity.HasIndex(o => new { o.PortfolioId, o.SubmittedAt }).IsDescending(false, true);
        });

        modelBuilder.Entity<BrokerConnection>(entity =>
        {
            entity.ToTable("broker_connections", t =>
            {
                t.HasCheckConstraint("ck_broker_connections_broker", "broker IN ('alpaca')");
                // Alpaca Live stays impossible until the §17.9 gates pass (a reviewed migration relaxes this).
                t.HasCheckConstraint("ck_broker_connections_environment", "environment IN ('paper')");
                t.HasCheckConstraint("ck_broker_connections_status", "status IN ('connected', 'degraded', 'revoked')");
            });
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Broker).HasMaxLength(8);
            entity.Property(c => c.Environment).HasMaxLength(8);
            entity.Property(c => c.Status).HasMaxLength(12);
            entity.Property(c => c.AccountRef).HasMaxLength(64);
            entity.Property(c => c.KeySuffix).HasMaxLength(4);
            entity.Property(c => c.CredentialsKeyId).HasMaxLength(32);
            entity.Property(c => c.LastError).HasMaxLength(500);
            entity.HasIndex(c => new { c.UserId, c.Broker, c.Environment }).IsUnique().HasFilter("status <> 'revoked'").HasDatabaseName("ux_broker_connections_one_active");
        });

        modelBuilder.Entity<Fill>(entity =>
        {
            entity.ToTable("fills", t => t.HasCheckConstraint("ck_fills_slippage", "slippage_per_share >= 0"));
            entity.HasKey(f => f.Id);
            entity.HasOne<Order>().WithMany().HasForeignKey(f => f.OrderId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(f => f.Side).HasMaxLength(4);
            entity.Property(f => f.SimulationModel).HasMaxLength(16);
            foreach (var price in new[] { nameof(Fill.Quantity), nameof(Fill.Price), nameof(Fill.ReferencePrice), nameof(Fill.SlippagePerShare) })
            {
                entity.Property(price).HasPrecision(18, 6);
            }

            entity.HasIndex(f => new { f.OrderId, f.Sequence }).IsUnique();
            entity.HasIndex(f => new { f.PortfolioId, f.FilledAt }).IsDescending(false, true);
            entity.HasQueryFilter(f => ScopeUserId == null || Orders.Any(o => o.Id == f.OrderId && o.UserId == ScopeUserId));
        });
    }
}
