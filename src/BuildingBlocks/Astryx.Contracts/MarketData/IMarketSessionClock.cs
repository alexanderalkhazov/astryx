namespace Astryx.Contracts.MarketData;

/// <summary>Today's regular session in exchange time (§16.2); null on weekends and holidays.</summary>
public sealed record TradingSession(DateOnly Date, DateTimeOffset OpenAt, DateTimeOffset CloseAt, bool IsHalfDay);

/// <summary>Owned by MarketData (trading calendar); read by Trading for proposal expiry and order acceptance.</summary>
public interface IMarketSessionClock
{
    DateTimeOffset Now { get; }

    Task<TradingSession?> TodayAsync(CancellationToken cancellationToken);
}
