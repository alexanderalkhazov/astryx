using Astryx.Infrastructure.Persistence;
using Astryx.Modules.Watchlists.Domain;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Watchlists.Infrastructure.Persistence;

/// <summary>Watchlists module data in schema <c>watchlist</c> (SYSTEM_DESIGN.md §10.4).</summary>
public sealed class WatchlistsDbContext(DbContextOptions<WatchlistsDbContext> options) : ModuleDbContext(options)
{
    public const string SchemaName = "watchlist";

    public override string Schema => SchemaName;

    public DbSet<Watchlist> Watchlists => Set<Watchlist>();

    public DbSet<WatchlistItem> Items => Set<WatchlistItem>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Watchlist>(entity =>
        {
            entity.ToTable("watchlists", t => t.HasCheckConstraint("ck_watchlists_name_length", "char_length(name) BETWEEN 1 AND 40"));
            entity.HasKey(w => w.Id);
            entity.Property(w => w.Name).HasMaxLength(Watchlist.MaxNameLength);
            entity.Property(w => w.Version).HasDefaultValue(1).IsConcurrencyToken();
            entity.HasIndex(w => new { w.UserId, w.Name }).IsUnique();
            entity.HasIndex(w => w.UserId).IsUnique().HasFilter("is_default").HasDatabaseName("ux_watchlists_one_default");
        });

        modelBuilder.Entity<WatchlistItem>(entity =>
        {
            entity.ToTable("watchlist_items", t => t.HasCheckConstraint("ck_watchlist_items_note_length", "note IS NULL OR char_length(note) <= 280"));
            entity.HasKey(i => new { i.WatchlistId, i.InstrumentId });
            entity.Property(i => i.Note).HasMaxLength(WatchlistItem.MaxNoteLength);
            entity.HasOne<Watchlist>().WithMany().HasForeignKey(i => i.WatchlistId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(i => i.InstrumentId);
        });
    }
}
