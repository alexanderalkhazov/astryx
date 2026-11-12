using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Portfolio.Domain;
using Microsoft.EntityFrameworkCore;
using PortfolioEntity = Astryx.Modules.Portfolio.Domain.Portfolio;

namespace Astryx.Modules.Portfolio.Infrastructure.Persistence;

/// <summary>Portfolio module data in schema <c>portfolio</c> (SYSTEM_DESIGN.md §10.4).</summary>
public sealed class PortfolioDbContext(DbContextOptions<PortfolioDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "portfolio";

    public override string Schema => SchemaName;

    public DbSet<PortfolioEntity> Portfolios => Set<PortfolioEntity>();

    public DbSet<LedgerTransaction> Transactions => Set<LedgerTransaction>();

    public DbSet<Position> Positions => Set<Position>();

    public DbSet<PortfolioSnapshot> Snapshots => Set<PortfolioSnapshot>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PortfolioEntity>(entity =>
        {
            entity.ToTable("portfolios", t =>
            {
                // eToro Real stays impossible until the §17.9 gates pass (a reviewed migration relaxes this).
                t.HasCheckConstraint("ck_portfolios_mode", "mode IN ('simulated', 'etoro_demo')");
                t.HasCheckConstraint("ck_portfolios_cash", "cash_balance >= 0 AND reserved_cash >= 0 AND reserved_cash <= cash_balance");
            });
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Mode).HasMaxLength(16);
            entity.Property(p => p.BaseCurrency).HasMaxLength(3).IsFixedLength();
            entity.Property(p => p.Version).IsConcurrencyToken();
            entity.HasIndex(p => new { p.UserId, p.Mode }).IsUnique();
        });

        modelBuilder.Entity<LedgerTransaction>(entity =>
        {
            entity.ToTable("transactions", t => t.HasCheckConstraint(
                "ck_transactions_type", "type IN ('initial_deposit', 'buy', 'sell', 'fee', 'reset_close', 'reset_deposit', 'adjustment')"));
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Type).HasMaxLength(20);
            entity.Property(t => t.Currency).HasMaxLength(3).IsFixedLength();
            entity.Property(t => t.IdempotencyRef).HasMaxLength(200);
            entity.Property(t => t.Seq).UseIdentityAlwaysColumn();
            entity.HasIndex(t => new { t.PortfolioId, t.Seq });
            entity.Property(t => t.Description).HasMaxLength(500);
            entity.HasIndex(t => t.IdempotencyRef).IsUnique();
            entity.HasIndex(t => new { t.PortfolioId, t.OccurredAt }).IsDescending(false, true);
            entity.HasOne<PortfolioEntity>().WithMany().HasForeignKey(t => t.PortfolioId).OnDelete(DeleteBehavior.Cascade);

            // Ledger rows carry no user id; they inherit ownership from their portfolio (SYSTEM_DESIGN.md §18.5).
            entity.HasQueryFilter(t => ScopeUserId == null || Portfolios.Any(p => p.Id == t.PortfolioId && p.UserId == ScopeUserId));
        });

        modelBuilder.Entity<Position>(entity =>
        {
            entity.ToTable("positions", t =>
            {
                t.HasCheckConstraint("ck_positions_status", "status IN ('open', 'closed')");
                t.HasCheckConstraint("ck_positions_exit_reason", "exit_reason IS NULL OR exit_reason IN ('stop', 'target', 'manual', 'reset')");
                t.HasCheckConstraint("ck_positions_quantity", "quantity >= 0");
                t.HasCheckConstraint("ck_positions_closed", "(status = 'open' AND closed_at IS NULL) OR (status = 'closed' AND closed_at IS NOT NULL AND exit_reason IS NOT NULL)");
            });
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Symbol).HasMaxLength(12);
            entity.Property(p => p.Status).HasMaxLength(8);
            entity.Property(p => p.Quantity).HasPrecision(18, 6);
            entity.Property(p => p.AvgEntryPrice).HasPrecision(18, 6);
            entity.Property(p => p.CostBasis).HasPrecision(20, 4);
            entity.Property(p => p.StopPrice).HasPrecision(18, 6);
            entity.Property(p => p.TargetPrice).HasPrecision(18, 6);
            entity.Property(p => p.ExitPrice).HasPrecision(18, 6);
            entity.Property(p => p.ExitReason).HasMaxLength(8);
            entity.Property(p => p.RealizedPnl).HasPrecision(20, 4);
            entity.Property(p => p.FeesPaid).HasPrecision(20, 4);
            entity.Property(p => p.BrokerPositionId).HasMaxLength(64);
            entity.Property(p => p.Version).IsConcurrencyToken();
            entity.HasOne<PortfolioEntity>().WithMany().HasForeignKey(p => p.PortfolioId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(p => new { p.PortfolioId, p.InstrumentId }).IsUnique().HasFilter("status = 'open'").HasDatabaseName("ux_positions_one_open");
            entity.HasIndex(p => new { p.PortfolioId, p.Status });
            entity.HasIndex(p => new { p.PortfolioId, p.ClosedAt }).IsDescending(false, true).HasFilter("status = 'closed'").HasDatabaseName("ix_positions_closed");
            entity.HasQueryFilter(p => ScopeUserId == null || Portfolios.Any(o => o.Id == p.PortfolioId && o.UserId == ScopeUserId));
        });

        modelBuilder.Entity<PortfolioSnapshot>(entity =>
        {
            entity.ToTable("snapshots", t => t.HasCheckConstraint("ck_snapshots_kind", "kind IN ('eod', 'hourly')"));
            entity.HasKey(s => new { s.PortfolioId, s.Kind, s.SnapshotAt });
            entity.Property(s => s.Kind).HasMaxLength(8);
            entity.Property(s => s.BenchmarkClose).HasPrecision(18, 6);
            entity.HasOne<PortfolioEntity>().WithMany().HasForeignKey(s => s.PortfolioId).OnDelete(DeleteBehavior.Cascade);
            entity.HasQueryFilter(s => ScopeUserId == null || Portfolios.Any(o => o.Id == s.PortfolioId && o.UserId == ScopeUserId));
        });
    }
}
