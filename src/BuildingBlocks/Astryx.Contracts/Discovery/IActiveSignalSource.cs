namespace Astryx.Contracts.Discovery;

/// <summary>The highest-scoring active signal for a symbol, as the AI context needs it (SYSTEM_DESIGN.md §12.4).</summary>
public sealed record ActiveSignal(string Type, decimal Score, string Reason, string OrderType, decimal Entry, decimal Stop, decimal Target, DateTimeOffset EvaluatedAt);

/// <summary>Owned by Discovery, read by Ai through Contracts.</summary>
public interface IActiveSignalSource
{
    /// <summary>Null when the symbol has no active signal.</summary>
    Task<ActiveSignal?> GetAsync(string symbol, CancellationToken cancellationToken);
}
