using Astryx.Modules.MarketData.Application.Ingestion;

namespace Astryx.Modules.MarketData.Application.Market;

public enum MarketSessionState
{
    /// <summary>Overnight, weekends and holidays.</summary>
    Closed,

    /// <summary>Pre-market, 04:00 ET until the open.</summary>
    Pre,

    /// <summary>Regular session.</summary>
    Open,

    /// <summary>After hours: until 20:00 ET (17:00 on half days).</summary>
    Post,
}

/// <param name="OpensAt">The regular open of the current session, or of the next one when the market is closed.</param>
/// <param name="ClosesAt">That session's regular close (13:00 ET on half days).</param>
public sealed record MarketSessionInfo(string Exchange, MarketSessionState State, DateOnly SessionDate, DateTimeOffset OpensAt, DateTimeOffset ClosesAt, bool IsHalfDay);

/// <summary>
/// The US equity session from <c>market.trading_calendar</c> in New York time (SYSTEM_DESIGN.md §20.4): NodaTime handles
/// the DST switch, and half days close at 13:00 ET. Pre-market starts at 04:00 ET.
/// </summary>
public sealed class MarketSessionService(MarketClock clock)
{
    public async Task<MarketSessionInfo> GetAsync(CancellationToken cancellationToken)
    {
        var now = clock.Now;
        var today = await clock.SessionAsync(clock.Today, cancellationToken);
        if (today is { IsHoliday: false, OpenAt: { } open, CloseAt: { } close })
        {
            var preStart = MarketClock.At(today.SessionDate, 4, 0);
            var postEnd = MarketClock.At(today.SessionDate, today.IsHalfDay ? 17 : 20, 0);
            var state = now < preStart ? MarketSessionState.Closed
                : now < open ? MarketSessionState.Pre
                : now < close ? MarketSessionState.Open
                : now < postEnd ? MarketSessionState.Post
                : MarketSessionState.Closed;

            // Before 04:00 the day's session is still "next"; after the post-market it is over.
            if (state != MarketSessionState.Closed || now < preStart)
            {
                return new MarketSessionInfo(MarketClock.PrimaryMic, state, today.SessionDate, open, close, today.IsHalfDay);
            }
        }

        var next = await clock.NextSessionAsync(clock.Today.AddDays(1), cancellationToken)
            ?? throw new InvalidOperationException("No trading session found in the next two weeks; the calendar is missing.");
        return new MarketSessionInfo(MarketClock.PrimaryMic, MarketSessionState.Closed, next.SessionDate, next.OpenAt!.Value, next.CloseAt!.Value, next.IsHalfDay);
    }
}
