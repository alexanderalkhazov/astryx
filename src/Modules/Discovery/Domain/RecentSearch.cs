using Astryx.Infrastructure.Security;

namespace Astryx.Modules.Discovery.Domain;

/// <summary>Row in <c>discovery.recent_searches</c>: an instrument the user opened from search (SYSTEM_DESIGN.md §10.4).</summary>
public sealed class RecentSearch : IUserOwned
{
    /// <summary>The newest this many are kept per user; older rows are pruned on insert.</summary>
    public const int Keep = 10;

    public Guid UserId { get; set; }

    public Guid InstrumentId { get; set; }

    public DateTimeOffset SearchedAt { get; set; }
}
