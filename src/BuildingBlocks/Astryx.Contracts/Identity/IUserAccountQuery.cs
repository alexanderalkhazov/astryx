namespace Astryx.Contracts.Identity;

/// <summary>Read-only view of an account, implemented by the Identity module for other modules.</summary>
public interface IUserAccountQuery
{
    Task<UserAccountSummary?> FindAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Every account that is not deleted, for system-wide Activity items (strategy guard).</summary>
    Task<IReadOnlyList<Guid>> ListActiveIdsAsync(CancellationToken cancellationToken);
}

public sealed record UserAccountSummary(Guid Id, string Email, bool EmailVerified);
