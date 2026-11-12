namespace Astryx.Contracts.MarketData;

/// <summary>A daily bar for the quant engine. <paramref name="Final"/> is false for the forming current session.</summary>
public sealed record DailyBar(DateOnly SessionDate, decimal Open, decimal High, decimal Low, decimal Close, long Volume, bool Final);

/// <summary>Daily history of one instrument, oldest first, never gap-filled (SYSTEM_DESIGN.md §13.2, §20.6).</summary>
/// <param name="LastBarAt">When the last bar was true: the quote time for a forming bar, else the bar's start.</param>
/// <param name="Stale">The latest bar or quote is past its freshness budget (§20.5).</param>
/// <param name="MissingSessions">Trading sessions inside the range with no stored bar (from the trading calendar).</param>
public sealed record DailyHistory(Guid InstrumentId, string Symbol, IReadOnlyList<DailyBar> Bars, DateTimeOffset? LastBarAt, bool Stale, IReadOnlyList<DateOnly> MissingSessions);

/// <summary>Final closes at fixed distances back from each instrument's latest final session, for universe rankings.</summary>
/// <param name="Through">The newest final session across the universe.</param>
/// <param name="Closes">Symbol → one close per requested offset (null where the history is too short).</param>
public sealed record CloseLadder(DateOnly? Through, IReadOnlyDictionary<string, decimal?[]> Closes);

/// <summary>Price history owned by MarketData, read by Quant through Contracts (SYSTEM_DESIGN.md §7.6).</summary>
public interface IPriceHistoryProvider
{
    /// <summary>The latest <paramref name="maxBars"/> daily bars, including the forming session; null for an unknown symbol.</summary>
    Task<DailyHistory?> GetDailyAsync(string symbol, int maxBars, CancellationToken cancellationToken);

    /// <summary>For every active instrument, the final close <c>n</c> sessions back for each <c>n</c> in <paramref name="sessionsBack"/> (0 = latest).</summary>
    Task<CloseLadder> GetCloseLadderAsync(IReadOnlyList<int> sessionsBack, CancellationToken cancellationToken);
}
