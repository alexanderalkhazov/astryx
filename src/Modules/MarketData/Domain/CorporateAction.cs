namespace Astryx.Modules.MarketData.Domain;

/// <summary>Row in <c>market.corporate_actions</c>: splits and dividends used to adjust stored bars (SYSTEM_DESIGN.md §20.7).</summary>
public sealed class CorporateAction
{
    public Guid Id { get; set; }

    public Guid InstrumentId { get; set; }

    public required string ActionType { get; set; }

    public DateOnly ExDate { get; set; }

    /// <summary>New shares per old share for splits (4 for a 4:1 split, 0.1 for a 1:10 reverse split).</summary>
    public decimal? Ratio { get; set; }

    /// <summary>Per-share cash for dividends.</summary>
    public decimal? CashAmount { get; set; }

    public required string Source { get; set; }
}

public static class CorporateActionType
{
    public const string Split = "split";
    public const string ReverseSplit = "reverse_split";
    public const string CashDividend = "cash_dividend";
}
