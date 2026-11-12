namespace Astryx.Modules.Watchlists.Domain;

/// <summary>
/// Row in <c>watchlist.watchlist_items</c>. Ownership comes from the list: items are only ever read or written after
/// the owning <see cref="Watchlist"/> was found through the per-user filter.
/// </summary>
public sealed class WatchlistItem
{
    public const int MaxNoteLength = 280;

    public Guid WatchlistId { get; set; }

    /// <summary>Refers to <c>market.instruments</c>; modules do not share foreign keys across schemas (§9.2).</summary>
    public Guid InstrumentId { get; set; }

    public int SortOrder { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset AddedAt { get; set; }
}
