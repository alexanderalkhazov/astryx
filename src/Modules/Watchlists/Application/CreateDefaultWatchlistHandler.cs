using Astryx.Contracts.Events;
using Astryx.Contracts.Events.Identity;
using Astryx.Modules.Watchlists.Domain;
using Astryx.Modules.Watchlists.Infrastructure.Persistence;
using Astryx.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Watchlists.Application;

/// <summary>Creates the default watchlist for a new user. Idempotent (one default per user).</summary>
public sealed class CreateDefaultWatchlistHandler(WatchlistsDbContext db, TimeProvider timeProvider) : IIntegrationEventHandler<UserRegistered>
{
    public async Task HandleAsync(UserRegistered integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken)
    {
        if (await db.Watchlists.AnyAsync(w => w.UserId == integrationEvent.UserId && w.IsDefault, cancellationToken))
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        db.Watchlists.Add(new Watchlist
        {
            Id = Ids.New(timeProvider),
            UserId = integrationEvent.UserId,
            Name = Watchlist.DefaultName,
            IsDefault = true,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
