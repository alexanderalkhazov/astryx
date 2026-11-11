namespace Astryx.Contracts.Identity;

/// <summary>Read-only view of an account, implemented by the Identity module for other modules.</summary>
public interface IUserAccountQuery
{
    Task<UserAccountSummary?> FindAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed record UserAccountSummary(Guid Id, string Email, bool EmailVerified);
