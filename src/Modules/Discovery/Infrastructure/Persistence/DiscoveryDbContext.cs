using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Discovery.Domain;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Discovery.Infrastructure.Persistence;

/// <summary>Discovery module data in schema <c>discovery</c> (SYSTEM_DESIGN.md §10.4).</summary>
public sealed class DiscoveryDbContext(DbContextOptions<DiscoveryDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "discovery";

    public override string Schema => SchemaName;

    public DbSet<RecentSearch> RecentSearches => Set<RecentSearch>();

    public DbSet<ScannerRun> ScannerRuns => Set<ScannerRun>();

    public DbSet<Signal> Signals => Set<Signal>();

    public DbSet<UniverseMember> UniverseMembers => Set<UniverseMember>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RecentSearch>(entity =>
        {
            // instrument_id refers to market.instruments; modules do not share foreign keys across schemas (§9.2).
            entity.ToTable("recent_searches");
            entity.HasKey(r => new { r.UserId, r.InstrumentId });
            entity.HasIndex(r => new { r.UserId, r.SearchedAt }).IsDescending(false, true);
        });

        modelBuilder.Entity<ScannerRun>(entity =>
        {
            entity.ToTable("scanner_runs", t =>
            {
                t.HasCheckConstraint("ck_scanner_runs_trigger", "trigger IN ('schedule', 'manual', 'eod')");
                t.HasCheckConstraint("ck_scanner_runs_status", "status IN ('running', 'completed', 'completed_with_warnings', 'failed')");
            });
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Trigger).HasMaxLength(16);
            entity.Property(r => r.Status).HasMaxLength(32);
            entity.Property(r => r.UniverseCode).HasMaxLength(32);
            entity.Property(r => r.ErrorCode).HasMaxLength(16);
            entity.HasIndex(r => r.StartedAt).IsDescending();
        });

        modelBuilder.Entity<Signal>(entity =>
        {
            entity.ToTable("signals", t =>
            {
                t.HasCheckConstraint("ck_signals_type", "signal_type IN ('momentum', 'breakout', 'pullback', 'unusual_volume', 'earnings')");
                t.HasCheckConstraint("ck_signals_horizon", "horizon IN ('intraday', 'swing', 'position')");
                t.HasCheckConstraint("ck_signals_order_type", "order_type IN ('buy_stop', 'limit', 'market')");
                t.HasCheckConstraint("ck_signals_status", "status IN ('active', 'superseded', 'expired', 'invalidated')");
                t.HasCheckConstraint("ck_signals_levels", "stop_price < entry_price AND entry_price < target_price");
                t.HasCheckConstraint("ck_signals_score", "score BETWEEN 0 AND 100");
            });
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Symbol).HasMaxLength(12);
            entity.Property(s => s.StrategyName).HasMaxLength(80);
            entity.Property(s => s.SignalType).HasMaxLength(16);
            entity.Property(s => s.Horizon).HasMaxLength(16);
            entity.Property(s => s.OrderType).HasMaxLength(16);
            entity.Property(s => s.Status).HasMaxLength(16);
            entity.Property(s => s.Score).HasPrecision(5, 2);
            entity.Property(s => s.EntryPrice).HasPrecision(18, 6);
            entity.Property(s => s.StopPrice).HasPrecision(18, 6);
            entity.Property(s => s.TargetPrice).HasPrecision(18, 6);
            entity.Property(s => s.ScoreBreakdown).HasColumnType("jsonb");
            entity.Property(s => s.Metrics).HasColumnType("jsonb");
            entity.HasOne<ScannerRun>().WithMany().HasForeignKey(s => s.ScannerRunId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(s => new { s.ScannerRunId, s.InstrumentId, s.StrategyVersionId }).IsUnique();
            entity.HasIndex(s => new { s.Status, s.Score }).IsDescending(false, true).HasFilter("status = 'active'").HasDatabaseName("ix_signals_active_by_score");
            entity.HasIndex(s => new { s.InstrumentId, s.EvaluatedAt }).IsDescending(false, true);
        });

        modelBuilder.Entity<UniverseMember>(entity =>
        {
            entity.ToTable("universe_members");
            entity.HasKey(m => new { m.UniverseCode, m.InstrumentId });
            entity.Property(m => m.UniverseCode).HasMaxLength(32);
        });
    }
}
