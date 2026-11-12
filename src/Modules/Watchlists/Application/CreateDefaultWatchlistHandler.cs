using Astryx.Contracts.Events;
using Astryx.Contracts.Events.Identity;
using Astryx.Modules.Watchlists.Infrastructure.Persistence;

namespace Astryx.Modules.Watchlists.Application;

/// <summary>Creates the default watchlist for a new user. Idempotent (one default per user).</summary>
public sealed class CreateDefaultWatchlistHandler(WatchlistsDbContext db, TimeProvider timeProvider) : IIntegrationEventHandler<UserRegistered>
{
    public Task HandleAsync(UserRegistered integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken) =>
        DefaultWatchlist.EnsureAsync(db, integrationEvent.UserId, timeProvider, cancellationToken);
}
