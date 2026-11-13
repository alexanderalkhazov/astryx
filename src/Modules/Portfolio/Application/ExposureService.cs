using Astryx.Contracts.MarketData;
using Astryx.Contracts.Paper;
using Astryx.Contracts.Profiles;
using Astryx.Contracts.Trading;
using Astryx.Infrastructure.Security;
using Astryx.Modules.Portfolio.Domain;
using Astryx.Modules.Portfolio.Infrastructure.Persistence;
using Astryx.SharedKernel.Finance;
using Astryx.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Astryx.Modules.Portfolio.Application;

public sealed record SectorSliceDto(string Sector, decimal Value, decimal Pct, bool Warning, bool OverLimit);

public sealed record AssetSliceDto(string Asset, decimal Value, decimal Pct);

/// <summary>P7 (SYSTEM_DESIGN.md §11.8). Warnings mark sectors above the warning line; nothing is ever sold for it.</summary>
public sealed record AllocationDto(IReadOnlyList<SectorSliceDto> Sectors, IReadOnlyList<AssetSliceDto> Assets, decimal SectorWarningPct, decimal MaxSectorExposurePct);

public sealed record ConcentrationWarningDto(string Kind, string Subject, decimal Pct, decimal Limit, string Message);

/// <summary>P8. <c>betaWeightedPct</c> is null when any open position's beta is missing.</summary>
public sealed record ExposureDto(decimal GrossPct, decimal? BetaWeightedPct, decimal OpenRiskToStops, IReadOnlyList<ConcentrationWarningDto> ConcentrationWarnings);

/// <summary>P9 item: what needs the user's attention, with a deep link (two levels at most).</summary>
public sealed record AttentionItemDto(string Kind, string Title, string Body, string DeepLink, string Severity);

public sealed record AttentionDto(IReadOnlyList<AttentionItemDto> Items);

/// <summary>
/// Allocation, exposure and attention (§8.4.6, §11.8 P7–P9), from the same valuation as P1. Position weight above
/// the cap and sector exposure above its limit are flagged only: the app never auto-sells.
/// </summary>
public sealed class ExposureService(
    PortfolioDbContext db, ICurrentUser user, ValuationService valuation, IStockResearchSource research, IRiskSettingsSource riskSettings,
    IPendingProposals proposals, IOpenOrders orders)
{
    public async Task<Result<AllocationDto>> AllocationAsync(CancellationToken cancellationToken)
    {
        if (await LoadAsync(cancellationToken) is not { } state)
        {
            return ValuationService.NotFound;
        }

        var (v, open, limits) = state;
        var equity = v.Portfolio.Equity;
        var sectors = open.GroupBy(p => v.Marks[p.InstrumentId].Sector ?? "Unclassified")
            .Select(g => (Sector: g.Key, Value: g.Sum(p => Value(p, v))))
            .OrderByDescending(x => x.Value)
            .Select(x => new SectorSliceDto(x.Sector, Money(x.Value), Pct(x.Value, equity), Fraction(x.Value, equity) > limits.SectorWarningPct, Fraction(x.Value, equity) > limits.MaxSectorExposurePct))
            .ToList();
        var assets = new List<AssetSliceDto> { new("stocks", v.Portfolio.Invested, Pct(v.Portfolio.Invested, equity)), new("cash", v.Portfolio.Cash, Pct(v.Portfolio.Cash, equity)) };
        return new AllocationDto(sectors, assets, limits.SectorWarningPct, limits.MaxSectorExposurePct);
    }

    public async Task<Result<ExposureDto>> ExposureAsync(CancellationToken cancellationToken)
    {
        if (await LoadAsync(cancellationToken) is not { } state)
        {
            return ValuationService.NotFound;
        }

        var (v, open, limits) = state;
        var equity = v.Portfolio.Equity;
        decimal? betaWeighted = 0m;
        foreach (var p in open)
        {
            var beta = (await research.GetFundamentalsAsync(p.Symbol, cancellationToken))?.Beta;
            betaWeighted = beta is { } b && betaWeighted is { } sum ? sum + (b * Value(p, v)) : null;
        }

        var warnings = new List<ConcentrationWarningDto>();
        foreach (var p in open.Where(p => Fraction(Value(p, v), equity) > limits.MaxPositionWeightPct))
        {
            var pct = Pct(Value(p, v), equity);
            warnings.Add(new("position_weight", p.Symbol, pct, limits.MaxPositionWeightPct, $"{p.Symbol} is {Percent(pct)} of the portfolio, above the {Percent(limits.MaxPositionWeightPct)} cap. It is not sold automatically."));
        }

        foreach (var g in open.GroupBy(p => v.Marks[p.InstrumentId].Sector ?? "Unclassified").Where(g => Fraction(g.Sum(p => Value(p, v)), equity) > limits.MaxSectorExposurePct))
        {
            var pct = Pct(g.Sum(p => Value(p, v)), equity);
            warnings.Add(new("sector_exposure", g.Key, pct, limits.MaxSectorExposurePct, $"{g.Key} is {Percent(pct)} of the portfolio, above the {Percent(limits.MaxSectorExposurePct)} limit."));
        }

        var openRisk = open.Sum(p => p.StopPrice is { } stop ? p.Quantity * Math.Max((v.Marks[p.InstrumentId].Last ?? p.AvgEntryPrice) - stop, 0m) : 0m);
        return new ExposureDto(Pct(v.Portfolio.Invested, equity), betaWeighted is { } bw && open.Count > 0 ? Pct(bw, equity) : open.Count == 0 ? 0m : null, Money(openRisk), warnings);
    }

    public async Task<Result<AttentionDto>> AttentionAsync(CancellationToken cancellationToken)
    {
        if (await LoadAsync(cancellationToken) is not { } state)
        {
            return ValuationService.NotFound;
        }

        var (v, open, limits) = state;
        var items = new List<AttentionItemDto>();
        if (v.Portfolio.Halted)
        {
            items.Add(new("halted", "New entries paused", $"Today is {Percent(v.Portfolio.DayPnlPct ?? 0m)}, at the {Percent(-limits.DailyLossHaltPct)} daily loss halt. Entries resume next session.", "/portfolio", "error"));
        }

        foreach (var p in await proposals.ListAsync(user.RequireUserId(), cancellationToken))
        {
            items.Add(new("pending_proposal", $"{p.Symbol} proposal waiting", "Review it before it expires.", $"/proposal/{p.Id}", "info"));
        }

        foreach (var p in open)
        {
            var last = v.Marks[p.InstrumentId].Last;
            if (last is { } l && p.StopPrice is { } stop && l > stop && (l - stop) / l <= 0.015m)
            {
                items.Add(new("stop_proximity", $"{p.Symbol} near its stop", $"{Percent(Math.Round((l - stop) / l, 4, MidpointRounding.ToEven))} above the stop at {stop:F2}.", $"/position/{p.Symbol}", "warning"));
            }
        }

        foreach (var o in await orders.ListWorkingEntriesAsync(v.Portfolio.Id, cancellationToken))
        {
            items.Add(new("working_order", $"{o.Symbol} buy stop working", $"{Math.Round(o.Quantity, 0)} shares trigger at {o.Trigger:F2}. It expires at the close if not triggered.", "/activity", "info"));
        }

        return new AttentionDto(items);
    }

    private async Task<(Valuation Valuation, List<Position> Open, RiskLimits Limits)?> LoadAsync(CancellationToken cancellationToken)
    {
        var portfolio = await db.Portfolios.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == user.RequireUserId() && p.Mode == PortfolioMode.Simulated, cancellationToken);
        if (portfolio is null)
        {
            return null;
        }

        var v = await valuation.ValueAsync(portfolio, cancellationToken);
        var open = await db.Positions.IgnoreQueryFilters().AsNoTracking().Where(p => p.PortfolioId == portfolio.Id && p.Status == PositionStatus.Open).ToListAsync(cancellationToken);
        return (v, open, await riskSettings.GetAsync(portfolio.UserId, cancellationToken));
    }

    private static decimal Value(Position p, Valuation v) => p.Quantity * (v.Marks[p.InstrumentId].Last ?? p.AvgEntryPrice);

    private static decimal Fraction(decimal part, decimal whole) => whole > 0 ? part / whole : 0m;

    private static decimal Pct(decimal part, decimal whole) => Math.Round(Fraction(part, whole), 4, MidpointRounding.ToEven);

    private static decimal Money(decimal v) => RoundingPolicy.Ledger(v);

    private static string Percent(decimal fraction) => $"{Math.Round(fraction * 100, 1, MidpointRounding.ToEven).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}%";
}
