namespace Astryx.Contracts.Portfolio;

/// <summary>Owned by Portfolio: which portfolio a user trades in.</summary>
public interface IPortfolioDirectory
{
    /// <summary>The user's simulated paper portfolio, or null before registration provisioned it.</summary>
    Task<Guid?> GetSimulatedAsync(Guid userId, CancellationToken cancellationToken);
}
