using Astryx.Contracts.Identity;
using Astryx.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Identity.Application;

public sealed class UserAccountQuery(IdentityDbContext db) : IUserAccountQuery
{
    public Task<UserAccountSummary?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.DeletedAt == null)
            .Select(u => new UserAccountSummary(u.Id, u.Email, u.EmailConfirmed))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListActiveIdsAsync(CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking().Where(u => u.DeletedAt == null).OrderBy(u => u.Id).Select(u => u.Id).ToListAsync(cancellationToken);
}
