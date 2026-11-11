using Astryx.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Astryx.Modules.Identity.Application;

/// <summary>
/// Current security-stamp fingerprints, cached for 60 s. Rotating the stamp (logout everywhere, password change)
/// invalidates outstanding access tokens: immediately on this instance, within 60 s on others (SYSTEM_DESIGN.md §18.3).
/// </summary>
public sealed class SecurityStampCache(IServiceScopeFactory scopes, IMemoryCache cache)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    public async Task<bool> IsCurrentAsync(Guid userId, string fingerprint, CancellationToken cancellationToken)
    {
        var current = await cache.GetOrCreateAsync(Key(userId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Lifetime;
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var stamp = await db.Users.AsNoTracking()
                .Where(u => u.Id == userId && u.DeletedAt == null && u.EmailConfirmed)
                .Select(u => u.SecurityStamp)
                .SingleOrDefaultAsync(cancellationToken);
            return stamp is null ? null : AccessTokenIssuer.StampFingerprint(stamp);
        });
        return current is not null && string.Equals(current, fingerprint, StringComparison.Ordinal);
    }

    public void Invalidate(Guid userId) => cache.Remove(Key(userId));

    private static string Key(Guid userId) => $"identity:sstamp:{userId}";
}
