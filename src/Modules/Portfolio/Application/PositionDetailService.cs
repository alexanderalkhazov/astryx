using Astryx.Contracts.MarketData;
using Astryx.Contracts.Paper;
using Astryx.Contracts.Trading;
using Astryx.Infrastructure.Security;
using Astryx.Modules.Portfolio.Domain;
using Astryx.Modules.Portfolio.Infrastructure.Persistence;
using Astryx.SharedKernel.Finance;
using Astryx.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Portfolio.Application;

/// <summary>The stop → entry → current → target track (design 10). <c>progressPct</c> runs 0 at the stop to 1 at the target.</summary>
public sealed record LevelTrackDto(decimal? Stop, decimal Entry, decimal? Current, decimal? Target, decimal? ProgressPct);

public sealed record GeneratedByDto(string Source, Guid? StrategyId, string? StrategyName, DateTimeOffset? At);

/// <summary>P3 (SYSTEM_DESIGN.md §11.8): facts, the level track and lineage.</summary>
public sealed record PositionDetailDto(
    Guid Id, string Symbol, string Name, string Status, bool IsNew, decimal Quantity, decimal AvgEntryPrice, decimal CostBasis, decimal? Last, decimal? MarketValue,
    decimal? UnrealizedPnl, decimal? UnrealizedPct, LevelTrackDto Track, decimal FeesPaid, DateTimeOffset OpenedAt, string? EntryOrderId, GeneratedByDto? GeneratedBy,
    decimal? ExitPrice, string? ExitReason, decimal? RealizedPnl, DateTimeOffset? ClosedAt, DateTimeOffset? QuoteAsOf);

/// <summary>P3 by id or symbol. Viewing a position clears its NEW badge.</summary>
public sealed class PositionDetailService(PortfolioDbContext db, ICurrentUser user, IInstrumentDirectory instruments, IOrderLookup orders, IProposalLineage lineage, TimeProvider timeProvider)
{
    public static readonly Error NotFound = new("not_found", "No position with that id.");

    public async Task<Result<PositionDetailDto>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await Positions().FirstOrDefaultAsync(p => p.Id == id, cancellationToken) is { } p ? await DetailAsync(p, cancellationToken) : NotFound;

    /// <summary>The open position for the symbol, else the latest closed one, else 404.</summary>
    public async Task<Result<PositionDetailDto>> BySymbolAsync(string symbol, CancellationToken cancellationToken)
    {
        var normalized = symbol.Trim().ToUpperInvariant();
        var p = await Positions().Where(x => x.Symbol == normalized).OrderBy(x => x.Status == PositionStatus.Open ? 0 : 1).ThenByDescending(x => x.ClosedAt).FirstOrDefaultAsync(cancellationToken);
        return p is null ? NotFound : await DetailAsync(p, cancellationToken);
    }

    private IQueryable<Position> Positions()
    {
        var userId = user.RequireUserId();
        return db.Positions.IgnoreQueryFilters().Where(p => db.Portfolios.IgnoreQueryFilters().Any(o => o.Id == p.PortfolioId && o.UserId == userId));
    }

    private async Task<PositionDetailDto> DetailAsync(Position p, CancellationToken cancellationToken)
    {
        if (p.SeenAt is null)
        {
            var now = timeProvider.GetUtcNow();
            await db.Positions.IgnoreQueryFilters().Where(x => x.Id == p.Id && x.SeenAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.SeenAt, now), cancellationToken);
        }

        var instrument = (await instruments.GetAsync([p.InstrumentId], cancellationToken)).FirstOrDefault();
        var open = p.Status == PositionStatus.Open;
        var last = open ? instrument?.Last : null;
        decimal? progress = last is { } l && p.StopPrice is { } stop && p.TargetPrice is { } target && target > stop ? Math.Clamp((l - stop) / (target - stop), 0m, 1m) : null;
        var origin = p.ProposalId is { } proposalId ? await lineage.GetAsync(proposalId, cancellationToken) : null;
        return new PositionDetailDto(
            p.Id, p.Symbol, instrument?.Name ?? p.Symbol, p.Status, p.SeenAt is null, Math.Round(p.Quantity, 0), Px(p.AvgEntryPrice), RoundingPolicy.Ledger(p.CostBasis),
            Px(last), last is { } lv ? RoundingPolicy.Ledger(p.Quantity * lv) : null,
            last is { } lu ? RoundingPolicy.Ledger(p.Quantity * (lu - p.AvgEntryPrice)) : null, last is { } lp ? Math.Round((lp / p.AvgEntryPrice) - 1, 4, MidpointRounding.ToEven) : null,
            new LevelTrackDto(Px(p.StopPrice), Px(p.AvgEntryPrice), Px(last), Px(p.TargetPrice), progress is { } pr ? Math.Round(pr, 4, MidpointRounding.ToEven) : null),
            RoundingPolicy.Ledger(p.FeesPaid), p.OpenedAt, p.EntryOrderId is { } orderId ? await orders.BrokerOrderIdAsync(orderId, cancellationToken) : null,
            origin is null ? null : new GeneratedByDto(origin.Source, origin.StrategyId, origin.StrategyName, origin.SourceAt),
            Px(p.ExitPrice), p.ExitReason, p.RealizedPnl is { } r ? RoundingPolicy.Ledger(r) : null, p.ClosedAt, instrument?.AsOf);
    }

    private static decimal Px(decimal v) => Math.Round(v, 2, MidpointRounding.ToEven);

    private static decimal? Px(decimal? v) => v is { } x ? Math.Round(x, 2, MidpointRounding.ToEven) : null;
}
