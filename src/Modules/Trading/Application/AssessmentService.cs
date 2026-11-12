using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Astryx.Contracts.Ai;
using Astryx.Contracts.Discovery;
using Astryx.Contracts.MarketData;
using Astryx.Contracts.Portfolio;
using Astryx.Contracts.Profiles;
using Astryx.Contracts.Quant;
using Astryx.Modules.Trading.Domain;
using Astryx.Modules.Trading.Domain.Risk;
using Astryx.Modules.Trading.Infrastructure.Persistence;
using Astryx.SharedKernel;
using Astryx.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Astryx.Modules.Trading.Application;

public sealed record AssessmentLimitsDto(decimal ByRisk, decimal ByWeightCap, decimal ByCash);

public sealed record AssessmentQuoteDto(decimal Last, DateTimeOffset AsOf);

public sealed record AssessmentImpactDto(decimal NewWeightPct, string? Sector, decimal SectorExposureAfterPct, decimal CashAfter, int OpenPositionsAfter);

public sealed record RiskCheckDto(string Code, string Status, string Message, decimal? Actual, decimal? Limit);

/// <summary>
/// T4 response (SYSTEM_DESIGN.md §11.7). <c>stale</c> is true once it expired (60 s) or its inputs changed (quote,
/// portfolio, risk settings or proposal), so the client must re-assess before approving.
/// </summary>
public sealed record RiskAssessmentDto(
    Guid Id, Guid ProposalId, string InputsHash, decimal Quantity, string SizedBy, AssessmentLimitsDto Limits, decimal RiskPerTradePct,
    AssessmentQuoteDto Quote, decimal Equity, decimal CashAvailable, decimal CapitalRequired, decimal MaxLoss, decimal MaxLossInclCosts,
    decimal PotentialProfit, decimal RewardRisk, decimal EstFees, decimal EstSlippage, AssessmentImpactDto Impact, IReadOnlyList<RiskCheckDto> Checks,
    bool Approvable, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, bool Stale);

/// <summary>T4 body. <c>quantity</c> is a whole number as a string; omit it for default sizing.</summary>
public sealed record AssessProposalRequest(string? Quantity, decimal RiskPerTradePct);

/// <summary>
/// Gathers a proposal's risk inputs through Contracts, runs <see cref="RiskEngine"/> and stores the immutable result
/// (§14.5). Used on proposal creation (default sizing), by T4, and again at execution (T-084).
/// </summary>
public sealed class AssessmentService(
    TradingDbContext db,
    ISignalLookup signals,
    IAnalysisLookup analyses,
    IInstrumentDirectory instruments,
    IStockResearchSource research,
    IQuantFacts quant,
    IPortfolioStateSource portfolios,
    IRiskSettingsSource riskSettings,
    IMarketSessionClock sessions,
    IConfiguration configuration)
{
    public static readonly IReadOnlyList<decimal> RiskChoices = [0.0050m, 0.0075m, 0.0100m];
    public static readonly TimeSpan Validity = TimeSpan.FromSeconds(60);

    public static readonly Error NotFound = new("not_found", "No proposal with that id.");
    public static readonly Error NotPending = new("proposal_not_pending", "This proposal is no longer pending.");
    public static readonly Error QuoteMissing = new("quote_stale", "There is no quote for this instrument yet.");

    /// <summary>The design's 0.75% when the user's max allows it, else the largest allowed choice (or the max itself).</summary>
    public static decimal DefaultRisk(RiskLimits limits) =>
        RiskChoices.Where(c => c <= limits.MaxRiskPerTradePct).DefaultIfEmpty(limits.MaxRiskPerTradePct).Max() is var best && best > 0.0075m ? 0.0075m : best;

    /// <summary>T4: validates the user's choice against the allowed set and their max (RISK-06), then assesses.</summary>
    public async Task<Result<RiskAssessmentDto>> AssessAsync(Guid proposalId, AssessProposalRequest request, CancellationToken cancellationToken)
    {
        if (await db.Proposals.AsNoTracking().FirstOrDefaultAsync(p => p.Id == proposalId, cancellationToken) is not { } proposal)
        {
            return NotFound;
        }

        decimal? quantity = null;
        if (request.Quantity is { } text)
        {
            if (!decimal.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var q) || q < 1 || q > 1_000_000)
            {
                return Error.Validation("quantity must be a whole number of at least 1.");
            }

            quantity = q;
        }

        var limits = await riskSettings.GetAsync(proposal.UserId, cancellationToken);
        if (!RiskChoices.Contains(request.RiskPerTradePct))
        {
            return Error.Validation("riskPerTradePct must be 0.0050, 0.0075 or 0.0100.");
        }

        if (request.RiskPerTradePct > limits.MaxRiskPerTradePct)
        {
            return Error.Validation($"riskPerTradePct {request.RiskPerTradePct.ToString("0.00##", CultureInfo.InvariantCulture)} is above your {limits.MaxRiskPerTradePct.ToString("0.00##", CultureInfo.InvariantCulture)} limit.");
        }

        return await CreateAsync(proposal, quantity, request.RiskPerTradePct, cancellationToken);
    }

    /// <summary>Assesses and stores. Refuses a proposal that is no longer pending or has expired.</summary>
    public async Task<Result<RiskAssessmentDto>> CreateAsync(Proposal proposal, decimal? quantity, decimal? riskPerTradePct, CancellationToken cancellationToken)
    {
        var now = sessions.Now;
        if (proposal.Status != ProposalStatus.Pending || proposal.ExpiresAt <= now)
        {
            return NotPending;
        }

        var gathered = await GatherAsync(proposal, quantity, riskPerTradePct, cancellationToken);
        if (!gathered.IsSuccess)
        {
            return gathered.Error;
        }

        var (inputs, context) = gathered.Value;
        var result = RiskEngine.Assess(inputs);
        var assessment = new RiskAssessment
        {
            Id = Ids.New(),
            ProposalId = proposal.Id,
            UserId = proposal.UserId,
            InputsHash = InputsHash(proposal.Id, proposal.Version, result.Quantity, inputs.RiskPerTradePct, context.QuoteSeq, context.PortfolioVersion, context.RiskVersion),
            RequestedQuantity = quantity,
            RiskPerTradePct = inputs.RiskPerTradePct,
            Quantity = result.Quantity,
            SizedBy = result.SizedBy,
            MaxQtyByRisk = result.QuantityByRisk,
            MaxQtyByWeight = result.QuantityByWeight,
            MaxQtyByCash = result.QuantityByCash,
            QuoteLast = inputs.QuoteLast,
            QuoteAsOf = inputs.QuoteAsOf,
            QuoteSeq = context.QuoteSeq,
            Equity = inputs.Equity,
            CashAvailable = inputs.CashAvailable,
            PortfolioVersion = context.PortfolioVersion,
            RiskSettingsVersion = context.RiskVersion,
            ProposalVersion = proposal.Version,
            CapitalRequired = result.CapitalRequired,
            MaxLoss = result.MaxLoss,
            MaxLossInclCosts = result.MaxLossInclCosts,
            PotentialProfit = result.PotentialProfit,
            RewardRisk = Math.Round(result.RewardRisk, 4, MidpointRounding.ToEven),
            EstFees = result.EstFees,
            EstSlippage = result.EstSlippage,
            Impact = JsonSerializer.Serialize(new AssessmentImpactDto(
                Math.Round(result.NewWeight, 4, MidpointRounding.ToEven), inputs.Sector, Math.Round(result.SectorAfter, 4, MidpointRounding.ToEven),
                Math.Round(result.CashAfter, 2, MidpointRounding.ToEven), result.OpenPositionsAfter)),
            Checks = JsonSerializer.Serialize(result.Checks.Select(c => new RiskCheckDto(c.Code, c.Status, c.Message, c.Actual, c.Limit))),
            Approvable = result.Approvable,
            CreatedAt = now,
            ExpiresAt = now + Validity,
        };
        db.RiskAssessments.Add(assessment);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(assessment, stale: false);
    }

    /// <summary>The proposal's latest assessment with its live validity, or null before the first one.</summary>
    public async Task<RiskAssessmentDto?> LatestAsync(Proposal proposal, CancellationToken cancellationToken)
    {
        var latest = await db.RiskAssessments.AsNoTracking().Where(a => a.ProposalId == proposal.Id).OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        return latest is null ? null : ToDto(latest, stale: !await IsCurrentAsync(proposal, latest, cancellationToken));
    }

    /// <summary>§14.5: unexpired and the inputs hash recomputed from the current state still matches.</summary>
    public async Task<bool> IsCurrentAsync(Proposal proposal, RiskAssessment assessment, CancellationToken cancellationToken)
    {
        if (sessions.Now >= assessment.ExpiresAt)
        {
            return false;
        }

        var quote = await research.GetQuoteAsync(proposal.Symbol, cancellationToken);
        var portfolio = await portfolios.GetAsync(proposal.PortfolioId, cancellationToken);
        var limits = await riskSettings.GetAsync(proposal.UserId, cancellationToken);
        return quote is not null && portfolio is not null
            && InputsHash(proposal.Id, proposal.Version, assessment.Quantity, assessment.RiskPerTradePct, quote.QuoteSeq, portfolio.Version, limits.Version) == assessment.InputsHash;
    }

    /// <summary>SHA-256 of proposal id | version | Q | r | quote_seq | portfolio version | risk settings version (§14.5).</summary>
    public static string InputsHash(Guid proposalId, int proposalVersion, decimal quantity, decimal riskPct, long quoteSeq, long portfolioVersion, int riskVersion)
    {
        var canonical = string.Join('|', proposalId.ToString("N"), proposalVersion.ToString(CultureInfo.InvariantCulture), Canonical(quantity), Canonical(riskPct),
            quoteSeq.ToString(CultureInfo.InvariantCulture), portfolioVersion.ToString(CultureInfo.InvariantCulture), riskVersion.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private sealed record HashContext(long QuoteSeq, long PortfolioVersion, int RiskVersion);

    private async Task<Result<(RiskInputs Inputs, HashContext Context)>> GatherAsync(Proposal proposal, decimal? quantity, decimal? riskPerTradePct, CancellationToken cancellationToken)
    {
        if (await research.GetQuoteAsync(proposal.Symbol, cancellationToken) is not { } quote)
        {
            return QuoteMissing;
        }

        var portfolio = await portfolios.GetAsync(proposal.PortfolioId, cancellationToken)
            ?? throw new InvalidOperationException($"Proposal {proposal.Id} points at a missing portfolio.");
        var limits = await riskSettings.GetAsync(proposal.UserId, cancellationToken);
        var facts = await quant.GetAsync(proposal.Symbol, cancellationToken);

        var ids = portfolio.OpenPositions.Select(p => p.InstrumentId).Append(proposal.InstrumentId).Distinct().ToList();
        var summaries = (await instruments.GetAsync(ids, cancellationToken)).ToDictionary(s => s.Id);
        var sector = summaries.GetValueOrDefault(proposal.InstrumentId)?.Sector;
        decimal Value(OpenPositionView p) => p.Quantity * (summaries.GetValueOrDefault(p.InstrumentId)?.Last ?? 0m);
        var equity = portfolio.CashBalance + portfolio.OpenPositions.Sum(Value);
        var sectorValue = sector is null ? 0m : portfolio.OpenPositions.Where(p => summaries.GetValueOrDefault(p.InstrumentId)?.Sector == sector).Sum(Value);

        var sourceActive = proposal.Source == ProposalSource.Signal
            ? (await signals.GetAsync(proposal.SignalId!.Value, cancellationToken))?.Status == "active"
            : (await analyses.GetAsync(proposal.UserId, proposal.AnalysisResultId!.Value, cancellationToken)) is { Stale: false };

        var inputs = new RiskInputs(
            proposal.Symbol, proposal.OrderType, proposal.EntryPrice, proposal.StopPrice, proposal.TargetPrice,
            riskPerTradePct ?? DefaultRisk(limits), quantity,
            quote.Last, quote.AsOf, BarsStale: facts?.History.Stale ?? true, SignalActive: sourceActive,
            equity, portfolio.CashBalance - portfolio.ReservedCash, sector, sectorValue,
            portfolio.OpenPositions.Count, WorkingEntries: 0, // working entry orders arrive with the paper engine (T-084)
            HoldsSymbol: portfolio.OpenPositions.Any(p => p.InstrumentId == proposal.InstrumentId), WorkingEntryForSymbol: false,
            portfolio.StartOfDayEquity, facts?.Atr14, await sessions.TodayAsync(cancellationToken), sessions.Now, limits, Costs());
        return (inputs, new HashContext(quote.QuoteSeq, portfolio.Version, limits.Version));
    }

    private CostModel Costs() => new(
        configuration.GetValue("Paper:SlippageBps", CostModel.Default.SlippageBps),
        decimal.Parse(configuration["Paper:MinSlippage"] ?? "0.01", CultureInfo.InvariantCulture),
        decimal.Parse(configuration["Paper:CommissionPerOrder"] ?? "0.00", CultureInfo.InvariantCulture));

    private static string Canonical(decimal value) => value.ToString("0.############", CultureInfo.InvariantCulture);

    private static RiskAssessmentDto ToDto(RiskAssessment a, bool stale)
    {
        static decimal Money(decimal v) => Math.Round(v, 2, MidpointRounding.ToEven);
        static decimal Whole(decimal v) => Math.Round(v, 0, MidpointRounding.ToEven);
        return new RiskAssessmentDto(
            a.Id, a.ProposalId, a.InputsHash, Whole(a.Quantity), a.SizedBy, new AssessmentLimitsDto(Whole(a.MaxQtyByRisk), Whole(a.MaxQtyByWeight), Whole(a.MaxQtyByCash)),
            Math.Round(a.RiskPerTradePct, 4, MidpointRounding.ToEven), new AssessmentQuoteDto(Math.Round(a.QuoteLast, 2, MidpointRounding.ToEven), a.QuoteAsOf),
            Money(a.Equity), Money(a.CashAvailable), Money(a.CapitalRequired), Money(a.MaxLoss), Money(a.MaxLossInclCosts), Money(a.PotentialProfit),
            Math.Round(a.RewardRisk, 2, MidpointRounding.ToEven), Money(a.EstFees), Money(a.EstSlippage),
            JsonSerializer.Deserialize<AssessmentImpactDto>(a.Impact)!, JsonSerializer.Deserialize<List<RiskCheckDto>>(a.Checks)!,
            a.Approvable, a.CreatedAt, a.ExpiresAt, stale);
    }
}
