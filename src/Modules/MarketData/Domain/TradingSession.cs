namespace Astryx.Modules.MarketData.Domain;

/// <summary>Row in <c>market.trading_calendar</c>: one exchange session; holidays have no open or close (SYSTEM_DESIGN.md §20.3).</summary>
public sealed class TradingSession
{
    public required string ExchangeMic { get; set; }

    public DateOnly SessionDate { get; set; }

    public DateTimeOffset? OpenAt { get; set; }

    public DateTimeOffset? CloseAt { get; set; }

    public bool IsHalfDay { get; set; }

    public bool IsHoliday { get; set; }
}
