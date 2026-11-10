using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Watchlists.Domain;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Watchlists.Infrastructure.Persistence;

/// <summary>Watchlists module data in schema <c>watchlist</c> (SYSTEM_DESIGN.md §10.4). Items arrive with T-047.</summary>
public sealed class WatchlistsDbContext(DbContextOptions<WatchlistsDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "watchlist";

    public override string Schema => SchemaName;

    public DbSet<Watchlist> Watchlists => Set<Watchlist>();

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Watchlist>(entity =>
        {
            entity.ToTable("watchlists", t => t.HasCheckConstraint("ck_watchlists_name_length", "char_length(name) BETWEEN 1 AND 40"));
            entity.HasKey(w => w.Id);
            entity.Property(w => w.Name).HasMaxLength(40);
            entity.HasIndex(w => new { w.UserId, w.Name }).IsUnique();
            entity.HasIndex(w => w.UserId).IsUnique().HasFilter("is_default").HasDatabaseName("ux_watchlists_one_default");
        });
}
