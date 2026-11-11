using Astryx.Infrastructure.Security;

namespace Astryx.Modules.Watchlists.Domain;

/// <summary>Row in <c>watchlist.watchlists</c>. Every user has exactly one default list (the design's star toggle).</summary>
public sealed class Watchlist : IUserOwned
{
    public const string DefaultName = "Watchlist";

    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public required string Name { get; set; }

    public bool IsDefault { get; set; }

    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
