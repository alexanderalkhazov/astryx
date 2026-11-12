using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Quant.Domain;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Quant.Infrastructure.Persistence;

/// <summary>Quant module data in schema <c>market</c> (SYSTEM_DESIGN.md §10.4): the indicator snapshot cache.</summary>
public sealed class QuantDbContext(DbContextOptions<QuantDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "market";

    public override string Schema => SchemaName;

    public DbSet<IndicatorSnapshotRecord> IndicatorSnapshots => Set<IndicatorSnapshotRecord>();

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<IndicatorSnapshotRecord>(entity =>
        {
            entity.ToTable("indicator_snapshots", t => t.HasCheckConstraint("ck_indicator_snapshots_data_quality", "data_quality IN ('ok', 'gaps', 'stale', 'insufficient')"));
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Timeframe).HasMaxLength(8);
            entity.Property(s => s.ParamsHash).HasMaxLength(64);
            entity.Property(s => s.DataQuality).HasMaxLength(16);
            entity.Property(s => s.Values).HasColumnType("jsonb");
            entity.HasIndex(s => new { s.InstrumentId, s.Timeframe, s.ParamsHash, s.AsOfBar }).IsUnique().HasDatabaseName("ux_indicator_snapshots_key");
        });
}
