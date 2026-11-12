using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Discovery.Domain;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Discovery.Infrastructure.Persistence;

/// <summary>Discovery module data in schema <c>discovery</c> (SYSTEM_DESIGN.md §10.4). Scanner runs and signals arrive in M3.</summary>
public sealed class DiscoveryDbContext(DbContextOptions<DiscoveryDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "discovery";

    public override string Schema => SchemaName;

    public DbSet<RecentSearch> RecentSearches => Set<RecentSearch>();

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<RecentSearch>(entity =>
        {
            // instrument_id refers to market.instruments; modules do not share foreign keys across schemas (§9.2).
            entity.ToTable("recent_searches");
            entity.HasKey(r => new { r.UserId, r.InstrumentId });
            entity.HasIndex(r => new { r.UserId, r.SearchedAt }).IsDescending(false, true);
        });
}
