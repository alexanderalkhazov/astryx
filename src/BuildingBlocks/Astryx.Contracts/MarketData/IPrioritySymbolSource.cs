namespace Astryx.Contracts.MarketData;

/// <summary>
/// Symbols that need fresh market data first (SYSTEM_DESIGN.md §16.3). Modules contribute their own: Portfolio (open
/// positions, priority 0), Trading (working orders, 1), Watchlists (2), Discovery (active signals, 3). Quote and news
/// sync visit lower <see cref="Priority"/> values first.
/// </summary>
public interface IPrioritySymbolSource
{
    int Priority { get; }

    Task<IReadOnlyList<string>> GetSymbolsAsync(CancellationToken cancellationToken);
}
