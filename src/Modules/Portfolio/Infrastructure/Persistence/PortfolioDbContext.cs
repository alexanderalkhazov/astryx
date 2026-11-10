using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Portfolio.Domain;
using Microsoft.EntityFrameworkCore;
using PortfolioEntity = Astryx.Modules.Portfolio.Domain.Portfolio;

namespace Astryx.Modules.Portfolio.Infrastructure.Persistence;

/// <summary>Portfolio module data in schema <c>portfolio</c> (SYSTEM_DESIGN.md §10.4). Positions and snapshots arrive with T-080/T-090.</summary>
public sealed class PortfolioDbContext(DbContextOptions<PortfolioDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "portfolio";

    public override string Schema => SchemaName;

    public DbSet<PortfolioEntity> Portfolios => Set<PortfolioEntity>();

    public DbSet<LedgerTransaction> Transactions => Set<LedgerTransaction>();

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
            entity.Property(t => t.Description).HasMaxLength(500);
            entity.HasIndex(t => t.IdempotencyRef).IsUnique();
            entity.HasIndex(t => new { t.PortfolioId, t.OccurredAt }).IsDescending(false, true);
            entity.HasOne<PortfolioEntity>().WithMany().HasForeignKey(t => t.PortfolioId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
