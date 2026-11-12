using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Strategies.Domain;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Strategies.Infrastructure.Persistence;

/// <summary>Strategies module data in schema <c>strategy</c> (SYSTEM_DESIGN.md §10.4).</summary>
public sealed class StrategiesDbContext(DbContextOptions<StrategiesDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "strategy";

    public override string Schema => SchemaName;

    public DbSet<Strategy> Strategies => Set<Strategy>();

    public DbSet<StrategyVersion> Versions => Set<StrategyVersion>();

    public DbSet<BacktestRun> Runs => Set<BacktestRun>();

    public DbSet<BacktestTrade> Trades => Set<BacktestTrade>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Strategy>(entity =>
        {
            entity.ToTable("strategies", t => t.HasCheckConstraint("ck_strategies_status", "status IN ('active', 'active_guarded', 'paused', 'retired')"));
            entity.HasKey(s => s.Id);
            entity.HasIndex(s => s.Code).IsUnique();
            entity.Property(s => s.Code).HasMaxLength(64);
            entity.Property(s => s.Name).HasMaxLength(80);
            entity.Property(s => s.Status).HasMaxLength(16);
            entity.Property(s => s.Rules).HasColumnType("jsonb");
            entity.Property(s => s.ParamSchema).HasColumnType("jsonb");
            entity.Property(s => s.IllustrativeLive).HasColumnType("jsonb");
            entity.HasOne<StrategyVersion>().WithMany().HasForeignKey(s => s.PublishedVersionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StrategyVersion>(entity =>
        {
            entity.ToTable("strategy_versions", t => t.HasCheckConstraint("ck_strategy_versions_state", "state IN ('draft', 'published', 'archived')"));
            entity.HasKey(v => v.Id);
            entity.HasIndex(v => new { v.StrategyId, v.VersionNumber }).IsUnique();
            entity.Property(v => v.State).HasMaxLength(16);
            entity.Property(v => v.CodeVersion).HasMaxLength(64);
            entity.Property(v => v.Params).HasColumnType("jsonb");
            entity.HasOne<Strategy>().WithMany().HasForeignKey(v => v.StrategyId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BacktestRun>(entity =>
        {
            entity.ToTable("backtest_runs", t => t.HasCheckConstraint("ck_backtest_runs_status", "status IN ('pending', 'running', 'completed', 'failed')"));
            entity.HasKey(r => r.Id);
            entity.HasIndex(r => new { r.StrategyId, r.RunNumber }).IsUnique();
            entity.Property(r => r.Status).HasMaxLength(16);
            entity.Property(r => r.InitialCapital).HasPrecision(20, 4);
            entity.Property(r => r.ErrorCode).HasMaxLength(64);
            foreach (var json in new[] { nameof(BacktestRun.Assumptions), nameof(BacktestRun.Metrics), nameof(BacktestRun.EquityCurve), nameof(BacktestRun.DrawdownCurve), nameof(BacktestRun.MonthlyReturns) })
            {
                entity.Property(json).HasColumnType("jsonb");
            }

            entity.HasOne<Strategy>().WithMany().HasForeignKey(r => r.StrategyId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<StrategyVersion>().WithMany().HasForeignKey(r => r.StrategyVersionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BacktestTrade>(entity =>
        {
            entity.ToTable("backtest_trades", t => t.HasCheckConstraint("ck_backtest_trades_segment", "segment IN ('is', 'oos')"));
            entity.HasKey(t => t.Id);
            entity.HasIndex(t => t.RunId);
            entity.Property(t => t.Segment).HasMaxLength(4);
            entity.Property(t => t.ExitReason).HasMaxLength(32);
            entity.Property(t => t.EntryPrice).HasPrecision(18, 6);
            entity.Property(t => t.ExitPrice).HasPrecision(18, 6);
            entity.Property(t => t.Quantity).HasPrecision(18, 6);
            entity.Property(t => t.Pnl).HasPrecision(20, 4);
            entity.Property(t => t.RMultiple).HasPrecision(8, 4);
            entity.HasOne<BacktestRun>().WithMany().HasForeignKey(t => t.RunId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
