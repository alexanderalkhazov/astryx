namespace Astryx.Contracts.Portfolio;

/// <summary>An open position that came from a strategy signal, with its owner (T-163).</summary>
public sealed record StrategyPosition(Guid UserId, Guid PositionId, string Symbol, Guid StrategyVersionId, DateTimeOffset OpenedAt);

/// <summary>Owned by Portfolio; read by the Strategies exit job through Contracts.</summary>
public interface IStrategyPositions
{
    /// <summary>Every open position with a strategy version, across users.</summary>
    Task<IReadOnlyList<StrategyPosition>> ListOpenAsync(CancellationToken cancellationToken);
}
