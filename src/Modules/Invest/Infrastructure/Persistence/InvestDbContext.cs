using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Invest.Domain;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Invest.Infrastructure.Persistence;

/// <summary>Invest module data in schema <c>invest</c> (TASKS.md T-164).</summary>
public sealed class InvestDbContext(DbContextOptions<InvestDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "invest";

    public override string Schema => SchemaName;

    public DbSet<InvestPlan> Plans => Set<InvestPlan>();

    public DbSet<Holding> Holdings => Set<Holding>();

    public DbSet<InvestTransaction> Transactions => Set<InvestTransaction>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InvestPlan>(entity =>
        {
            entity.ToTable("plans", t =>
            {
                t.HasCheckConstraint("ck_plans_profile", "profile IN ('cautious', 'balanced', 'growth')");
                t.HasCheckConstraint("ck_plans_frequency", "contribution_frequency IN ('none', 'weekly', 'monthly')");
                t.HasCheckConstraint("ck_plans_cash", "cash >= 0");
            });
            entity.HasKey(p => p.Id);
            entity.HasIndex(p => p.UserId).IsUnique();
            entity.Property(p => p.Profile).HasMaxLength(16);
            entity.Property(p => p.ContributionFrequency).HasMaxLength(16);
            entity.Property(p => p.Cash).HasPrecision(19, 4);
            entity.Property(p => p.Contributed).HasPrecision(19, 4);
            entity.Property(p => p.ContributionAmount).HasPrecision(19, 4);
            entity.Property(p => p.GoalAmount).HasPrecision(19, 4);
            entity.Property(p => p.Version).HasDefaultValue(1).IsConcurrencyToken();
            entity.HasIndex(p => p.NextContributionOn).HasFilter("contribution_frequency <> 'none'");
        });

        modelBuilder.Entity<Holding>(entity =>
        {
            entity.ToTable("holdings", t => t.HasCheckConstraint("ck_holdings_quantity", "quantity >= 0"));
            entity.HasKey(h => h.Id);
            entity.HasIndex(h => new { h.PlanId, h.Symbol }).IsUnique();
            entity.Property(h => h.Symbol).HasMaxLength(12);
            entity.Property(h => h.Quantity).HasPrecision(18, 6);
            entity.Property(h => h.CostBasis).HasPrecision(19, 4);
            entity.HasOne<InvestPlan>().WithMany().HasForeignKey(h => h.PlanId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InvestTransaction>(entity =>
        {
            entity.ToTable("transactions", t => t.HasCheckConstraint("ck_transactions_kind", "kind IN ('deposit', 'buy', 'sell')"));
            entity.HasKey(t => t.Id);
            entity.HasIndex(t => new { t.PlanId, t.OccurredAt });
            entity.Property(t => t.Kind).HasMaxLength(16);
            entity.Property(t => t.Symbol).HasMaxLength(12);
            entity.Property(t => t.Quantity).HasPrecision(18, 6);
            entity.Property(t => t.Price).HasPrecision(19, 4);
            entity.Property(t => t.Amount).HasPrecision(19, 4);
            entity.HasOne<InvestPlan>().WithMany().HasForeignKey(t => t.PlanId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
