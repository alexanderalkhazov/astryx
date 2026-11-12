using Astryx.Contracts.MarketData;
using Astryx.Infrastructure.Security;
using Astryx.Modules.Watchlists.Domain;
using Astryx.Modules.Watchlists.Infrastructure.Persistence;
using Astryx.SharedKernel;
using Astryx.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Astryx.Modules.Watchlists.Application;

/// <summary>A watched instrument with its latest price (SYSTEM_DESIGN.md §11.5 W1).</summary>
/// <param name="HasActiveSignal">Filled once the scanner exists (M3).</param>
/// <param name="Held">Open position: filled once Portfolio exists (M5).</param>
public sealed record WatchlistItemDto(
    string Symbol, string Name, string? Sector, decimal? Last, decimal? ChangePct, bool HasActiveSignal, bool Held, string? Note, DateTimeOffset AddedAt);

/// <param name="Version">Concurrency version; the list's ETag is <c>"v{Version}"</c>.</param>
public sealed record WatchlistDto(Guid Id, string Name, bool IsDefault, int SortOrder, int Version, IReadOnlyList<WatchlistItemDto> Items);

/// <summary>
/// The signed-in user's watchlists (SYSTEM_DESIGN.md §7.5, §11.5 W1–W8). A list reference is its id or
/// <c>default</c>. Lists are found through the per-user filter, and items are only touched through a list found that way.
/// </summary>
public sealed class WatchlistService(WatchlistsDbContext db, IInstrumentDirectory instruments, ICurrentUser currentUser, TimeProvider timeProvider)
{
    public const string DefaultRef = "default";

    public static readonly Error NotFound = new("not_found", "No watchlist with that id.");
    public static readonly Error InstrumentNotFound = new("instrument_not_found", "No instrument with that symbol.");
    public static readonly Error DuplicateName = new("duplicate", "You already have a watchlist with that name.");
    public static readonly Error DuplicateItem = new("duplicate", "That stock is already on this watchlist.");
    public static readonly Error TooManyLists = new("limit_reached", $"You can have at most {Watchlist.MaxLists} watchlists.");
    public static readonly Error TooManyItems = new("limit_reached", $"A watchlist holds at most {Watchlist.MaxItems} stocks.");
    public static readonly Error DefaultUndeletable = new("conflict", "The default watchlist can't be deleted.");
    public static readonly Error OrderMismatch = new("conflict", "The watchlist changed since you loaded it. Reload and try again.");
    public static readonly Error PreconditionRequired = new("precondition_required", "Send the If-Match header with the ETag you last read.");
    public static readonly Error PreconditionFailed = new("precondition_failed", "The watchlist changed since you loaded it. Reload and try again.");

    /// <summary>W1: the default list first, then by sort order and name.</summary>
    public async Task<IReadOnlyList<WatchlistDto>> ListAsync(CancellationToken cancellationToken)
    {
        await EnsureDefaultAsync(cancellationToken);
        var lists = await db.Watchlists.AsNoTracking()
            .OrderByDescending(w => w.IsDefault).ThenBy(w => w.SortOrder).ThenBy(w => w.Name)
            .ToListAsync(cancellationToken);
        var ids = lists.Select(w => w.Id).ToList();
        var items = await db.Items.AsNoTracking().Where(i => ids.Contains(i.WatchlistId)).ToListAsync(cancellationToken);
        var summaries = await SummariesAsync(items, cancellationToken);
        return lists.Select(w => ToDto(w, items.Where(i => i.WatchlistId == w.Id), summaries)).ToList();
    }

    /// <summary>W2.</summary>
    public async Task<Result<WatchlistDto>> CreateAsync(string name, CancellationToken cancellationToken)
    {
        await EnsureDefaultAsync(cancellationToken);
        name = name.Trim();
        if (await db.Watchlists.CountAsync(cancellationToken) >= Watchlist.MaxLists)
        {
            return TooManyLists;
        }

        if (await db.Watchlists.AnyAsync(w => w.Name == name, cancellationToken))
        {
            return DuplicateName;
        }

        var now = timeProvider.GetUtcNow();
        var list = new Watchlist
        {
            Id = Ids.New(timeProvider),
            UserId = currentUser.RequireUserId(),
            Name = name,
            SortOrder = (await db.Watchlists.MaxAsync(w => (int?)w.SortOrder, cancellationToken) ?? 0) + 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Watchlists.Add(list);
        if (!await TrySaveAsync(cancellationToken))
        {
            return DuplicateName; // a concurrent create took the name
        }

        return ToDto(list, [], new Dictionary<Guid, InstrumentSummary>());
    }

    /// <summary>W3: rename or move a list. Needs the current version.</summary>
    public async Task<Result<WatchlistDto>> UpdateAsync(string listRef, int? expectedVersion, string? name, int? sortOrder, CancellationToken cancellationToken)
    {
        if (expectedVersion is null)
        {
            return PreconditionRequired;
        }

        if (await FindAsync(listRef, tracked: true, cancellationToken) is not { } list)
        {
            return NotFound;
        }

        if (list.Version != expectedVersion)
        {
            return PreconditionFailed;
        }

        name = name?.Trim();
        if (name is not null && name != list.Name && await db.Watchlists.AnyAsync(w => w.Name == name, cancellationToken))
        {
            return DuplicateName;
        }

        list.Name = name ?? list.Name;
        list.SortOrder = sortOrder ?? list.SortOrder;
        list.Version++;
        list.UpdatedAt = timeProvider.GetUtcNow();
        try
        {
            if (!await TrySaveAsync(cancellationToken))
            {
                return DuplicateName;
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            return PreconditionFailed;
        }

        return await DtoAsync(list, cancellationToken);
    }

    /// <summary>W4. Items go with the list (cascade).</summary>
    public async Task<Result> DeleteAsync(string listRef, CancellationToken cancellationToken)
    {
        if (await FindAsync(listRef, tracked: true, cancellationToken) is not { } list)
        {
            return NotFound;
        }

        if (list.IsDefault)
        {
            return DefaultUndeletable;
        }

        db.Watchlists.Remove(list);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>W5: adds to the end of the list. An existing item is <see cref="DuplicateItem"/>.</summary>
    public async Task<Result<WatchlistItemDto>> AddItemAsync(string listRef, string symbol, string? note, CancellationToken cancellationToken)
    {
        if (await FindAsync(listRef, tracked: false, cancellationToken) is not { } list)
        {
            return NotFound;
        }

        if (await instruments.FindAsync(symbol, cancellationToken) is not { } instrument)
        {
            return InstrumentNotFound;
        }

        if (await db.Items.CountAsync(i => i.WatchlistId == list.Id, cancellationToken) >= Watchlist.MaxItems)
        {
            return TooManyItems;
        }

        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        var now = timeProvider.GetUtcNow();
        if (!await InsertItemAsync(list.Id, instrument.Id, note, now, cancellationToken))
        {
            return DuplicateItem;
        }

        return ToItem(new WatchlistItem { WatchlistId = list.Id, InstrumentId = instrument.Id, Note = note, AddedAt = now }, instrument);
    }

    /// <summary>W7: the star toggle on. Idempotent; a full default list is <see cref="TooManyItems"/>.</summary>
    public async Task<Result> StarAsync(string symbol, CancellationToken cancellationToken)
    {
        await EnsureDefaultAsync(cancellationToken);
        var result = await AddItemAsync(DefaultRef, symbol, note: null, cancellationToken);
        return result.IsSuccess || result.Error == DuplicateItem ? Result.Success() : result.Error;
    }

    /// <summary>W6 (and the star toggle off). Idempotent: an absent item or unknown symbol is still a success.</summary>
    public async Task<Result> RemoveItemAsync(string listRef, string symbol, CancellationToken cancellationToken)
    {
        if (await FindAsync(listRef, tracked: false, cancellationToken) is not { } list)
        {
            return NotFound;
        }

        if (await instruments.FindAsync(symbol, cancellationToken) is not { } instrument)
        {
            return Result.Success();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var removed = await db.Items.Where(i => i.WatchlistId == list.Id && i.InstrumentId == instrument.Id).ExecuteDeleteAsync(cancellationToken);
        if (removed > 0)
        {
            await BumpAsync(list.Id, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>W8: puts the items in the given order. The symbols must be exactly the list's items.</summary>
    public async Task<Result<WatchlistDto>> ReorderItemsAsync(string listRef, int? expectedVersion, IReadOnlyList<string> symbols, CancellationToken cancellationToken)
    {
        if (expectedVersion is null)
        {
            return PreconditionRequired;
        }

        if (await FindAsync(listRef, tracked: true, cancellationToken) is not { } list)
        {
            return NotFound;
        }

        if (list.Version != expectedVersion)
        {
            return PreconditionFailed;
        }

        var items = await db.Items.Where(i => i.WatchlistId == list.Id).ToListAsync(cancellationToken);
        var summaries = await SummariesAsync(items, cancellationToken);
        var bySymbol = items.Where(i => summaries.ContainsKey(i.InstrumentId))
            .ToDictionary(i => summaries[i.InstrumentId].Symbol, StringComparer.OrdinalIgnoreCase);
        var requested = symbols.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (requested.Count != symbols.Count || requested.Count != items.Count || !requested.All(bySymbol.ContainsKey))
        {
            return OrderMismatch;
        }

        for (var index = 0; index < requested.Count; index++)
        {
            bySymbol[requested[index]].SortOrder = index;
        }

        list.Version++;
        list.UpdatedAt = timeProvider.GetUtcNow();
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return PreconditionFailed;
        }

        return ToDto(list, items, summaries);
    }

    /// <summary>
    /// Creates the user's default list unless it exists. The <c>UserRegistered</c> handler does this too; a request
    /// that races account setup creates it here, and the partial unique index keeps it to one.
    /// </summary>
    private async Task EnsureDefaultAsync(CancellationToken cancellationToken)
    {
        if (!await db.Watchlists.AnyAsync(w => w.IsDefault, cancellationToken))
        {
            await DefaultWatchlist.EnsureAsync(db, currentUser.RequireUserId(), timeProvider, cancellationToken);
        }
    }

    private async Task<Watchlist?> FindAsync(string listRef, bool tracked, CancellationToken cancellationToken)
    {
        var query = tracked ? db.Watchlists : db.Watchlists.AsNoTracking();
        if (string.Equals(listRef, DefaultRef, StringComparison.OrdinalIgnoreCase))
        {
            await EnsureDefaultAsync(cancellationToken);
            return await query.SingleOrDefaultAsync(w => w.IsDefault, cancellationToken);
        }

        return Guid.TryParse(listRef, out var id) ? await query.SingleOrDefaultAsync(w => w.Id == id, cancellationToken) : null;
    }

    /// <summary>Appends the item and bumps the list version in one transaction; false if it was already there.</summary>
    private async Task<bool> InsertItemAsync(Guid watchlistId, Guid instrumentId, string? note, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO watchlist.watchlist_items (watchlist_id, instrument_id, sort_order, note, added_at)
            SELECT {watchlistId}, {instrumentId}, COALESCE(MAX(sort_order) + 1, 0), {note}, {now}
            FROM watchlist.watchlist_items WHERE watchlist_id = {watchlistId}
            ON CONFLICT (watchlist_id, instrument_id) DO NOTHING
            """,
            cancellationToken);
        if (inserted > 0)
        {
            await BumpAsync(watchlistId, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return inserted > 0;
    }

    private Task BumpAsync(Guid watchlistId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        return db.Watchlists.Where(w => w.Id == watchlistId)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Version, w => w.Version + 1).SetProperty(w => w.UpdatedAt, now), cancellationToken);
    }

    private async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    private async Task<WatchlistDto> DtoAsync(Watchlist list, CancellationToken cancellationToken)
    {
        var items = await db.Items.AsNoTracking().Where(i => i.WatchlistId == list.Id).ToListAsync(cancellationToken);
        return ToDto(list, items, await SummariesAsync(items, cancellationToken));
    }

    private async Task<Dictionary<Guid, InstrumentSummary>> SummariesAsync(IEnumerable<WatchlistItem> items, CancellationToken cancellationToken)
    {
        var ids = items.Select(i => i.InstrumentId).Distinct().ToList();
        return ids.Count == 0 ? [] : (await instruments.GetAsync(ids, cancellationToken)).ToDictionary(s => s.Id);
    }

    private static WatchlistDto ToDto(Watchlist list, IEnumerable<WatchlistItem> items, IReadOnlyDictionary<Guid, InstrumentSummary> summaries) =>
        new(list.Id, list.Name, list.IsDefault, list.SortOrder, list.Version,
            items.Where(i => summaries.ContainsKey(i.InstrumentId))
                .OrderBy(i => i.SortOrder).ThenBy(i => i.AddedAt)
                .Select(i => ToItem(i, summaries[i.InstrumentId]))
                .ToList());

    private static WatchlistItemDto ToItem(WatchlistItem item, InstrumentSummary s) =>
        new(s.Symbol, s.Name, s.Sector, s.Last, s.ChangePct, HasActiveSignal: false, Held: false, item.Note, item.AddedAt);
}
