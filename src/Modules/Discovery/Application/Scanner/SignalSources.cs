using Astryx.Contracts.Discovery;
using Astryx.Contracts.MarketData;
using Astryx.Modules.Discovery.Domain;
using Astryx.Modules.Discovery.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Discovery.Application.Scanner;

/// <summary>Chart levels (M6 <c>levels</c>) from the highest-scoring active signal for the symbol.</summary>
public sealed class SignalChartLevels(DiscoveryDbContext db) : IChartLevelsSource
{
    public async Task<ChartLevels?> GetLevelsAsync(string symbol, CancellationToken cancellationToken)
    {
        var normalized = symbol.Trim().ToUpperInvariant();
        return await db.Signals.AsNoTracking()
            .Where(s => s.Symbol == normalized && s.Status == SignalStatus.Active)
            .OrderByDescending(s => s.Score).ThenByDescending(s => s.EvaluatedAt)
            .Select(s => new ChartLevels(s.EntryPrice, s.StopPrice, s.TargetPrice))
            .FirstOrDefaultAsync(cancellationToken);
    }
}

/// <summary>The active signal behind the chart levels, for the AI context (SYSTEM_DESIGN.md §12.4).</summary>
public sealed class ActiveSignalSource(DiscoveryDbContext db) : IActiveSignalSource
{
    public async Task<ActiveSignal?> GetAsync(string symbol, CancellationToken cancellationToken)
    {
        var normalized = symbol.Trim().ToUpperInvariant();
        return await db.Signals.AsNoTracking()
            .Where(s => s.Symbol == normalized && s.Status == SignalStatus.Active)
            .OrderByDescending(s => s.Score).ThenByDescending(s => s.EvaluatedAt)
            .Select(s => new ActiveSignal(s.SignalType, s.Score, s.Reason, s.OrderType, s.EntryPrice, s.StopPrice, s.TargetPrice, s.EvaluatedAt))
            .FirstOrDefaultAsync(cancellationToken);
    }
}

/// <summary>Symbols with an active signal get fresh quotes and news after positions, orders and watchlists (§16.3).</summary>
public sealed class SignalPrioritySymbols(DiscoveryDbContext db) : IPrioritySymbolSource
{
    public int Priority => 3;

    public async Task<IReadOnlyList<string>> GetSymbolsAsync(CancellationToken cancellationToken) =>
        await db.Signals.AsNoTracking().Where(s => s.Status == SignalStatus.Active).Select(s => s.Symbol).Distinct().OrderBy(s => s).ToListAsync(cancellationToken);
}
