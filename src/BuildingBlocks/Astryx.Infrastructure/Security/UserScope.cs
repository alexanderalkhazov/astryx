namespace Astryx.Infrastructure.Security;

/// <summary>
/// The user whose data the current request may see (SYSTEM_DESIGN.md §18.5). Set by <see cref="UserScopeMiddleware"/>
/// for authenticated requests; every <see cref="IUserOwned"/> query in that request is filtered to this user.
/// Null for unauthenticated flows (login, refresh) and background workers, which address rows explicitly.
/// </summary>
public static class UserScope
{
    private static readonly AsyncLocal<Guid?> Current = new();

    public static Guid? UserId => Current.Value;

    /// <summary>Sets the scope until the returned handle is disposed (tests and future impersonation-free admin tools).</summary>
    public static IDisposable Begin(Guid? userId)
    {
        var previous = Current.Value;
        Current.Value = userId;
        return new Restore(previous);
    }

    internal static void Set(Guid? userId) => Current.Value = userId;

    private sealed class Restore(Guid? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}

/// <summary>An entity that belongs to one user. Module contexts filter it to <see cref="UserScope.UserId"/> automatically.</summary>
public interface IUserOwned
{
    Guid UserId { get; }
}

/// <summary>The signed-in user, for application services.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    bool IsAuthenticated => UserId is not null;

    Guid RequireUserId() => UserId ?? throw new InvalidOperationException("This operation requires a signed-in user.");
}

public sealed class CurrentUser : ICurrentUser
{
    public Guid? UserId => UserScope.UserId;
}
