using Astryx.Infrastructure.Security;

namespace Astryx.Modules.Trading.Domain;

/// <summary>
/// Row in <c>trading.risk_assessments</c> (SYSTEM_DESIGN.md §10.4, §14.5): one immutable evaluation. Valid while
/// approvable, unexpired (60 s) and its <see cref="InputsHash"/> still matches the current inputs.
/// </summary>
public sealed class RiskAssessment : IUserOwned
{
    public Guid Id { get; set; }

    public Guid ProposalId { get; set; }

    public Guid UserId { get; set; }

    public required string InputsHash { get; set; }

    public decimal? RequestedQuantity { get; set; }

    public decimal RiskPerTradePct { get; set; }

    public decimal Quantity { get; set; }

    public required string SizedBy { get; set; }

    public decimal MaxQtyByRisk { get; set; }

    public decimal MaxQtyByWeight { get; set; }

    public decimal MaxQtyByCash { get; set; }

    public decimal QuoteLast { get; set; }

    public DateTimeOffset QuoteAsOf { get; set; }

    public long QuoteSeq { get; set; }

    public decimal Equity { get; set; }

    public decimal CashAvailable { get; set; }

    public long PortfolioVersion { get; set; }

    public int RiskSettingsVersion { get; set; }

    public int ProposalVersion { get; set; }

    public decimal CapitalRequired { get; set; }

    public decimal MaxLoss { get; set; }

    public decimal MaxLossInclCosts { get; set; }

    public decimal PotentialProfit { get; set; }

    public decimal RewardRisk { get; set; }

    public decimal EstFees { get; set; }

    public decimal EstSlippage { get; set; }

    /// <summary>JSON: newWeightPct, sector, sectorExposureAfterPct, cashAfter, openPositionsAfter.</summary>
    public required string Impact { get; set; }

    /// <summary>JSON array of {code, status, message, actual, limit}.</summary>
    public required string Checks { get; set; }

    public bool Approvable { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}
