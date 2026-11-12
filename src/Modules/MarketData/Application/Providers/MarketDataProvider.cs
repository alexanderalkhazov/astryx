namespace Astryx.Modules.MarketData.Application.Providers;

/// <summary>
/// A source of market data (SYSTEM_DESIGN.md §16.1). Implementations return raw provider values; ingestion validates
/// them before anything is stored, so a provider may return a bad tick.
/// </summary>
public interface IMarketDataProvider
{
    /// <summary>Provider code stored in <c>source</c> columns, e.g. <c>fake</c>.</summary>
    string Code { get; }

    MarketDataCapabilities Capabilities { get; }

    Task<IReadOnlyList<ProviderQuote>> GetQuotesAsync(IReadOnlyList<string> symbols, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProviderBar>> GetBarsAsync(string symbol, BarInterval interval, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProviderInstrument>> GetInstrumentsAsync(string universeCode, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProviderCorporateAction>> GetCorporateActionsAsync(string symbol, DateOnly from, CancellationToken cancellationToken);

    Task<ProviderFundamentals?> GetFundamentalsAsync(string symbol, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProviderSession>> GetCalendarAsync(string exchangeMic, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

public interface INewsProvider
{
    Task<IReadOnlyList<ProviderNewsItem>> GetNewsAsync(string symbol, DateTimeOffset since, int limit, CancellationToken cancellationToken);
}

/// <summary>Thrown when the provider cannot serve a request; the API maps it to 503 <c>market_data_unavailable</c>.</summary>
public sealed class MarketDataUnavailableException(string provider, string reason)
    : Exception($"Market data provider '{provider}' is unavailable: {reason}")
{
    public string Provider { get; } = provider;

    public string Reason { get; } = reason;
}

public enum BarInterval
{
    OneMinute,
    FiveMinutes,
    FifteenMinutes,
    OneHour,
    OneDay,
}

public static class BarIntervals
{
    public static int Minutes(this BarInterval interval) => interval switch
    {
        BarInterval.OneMinute => 1,
        BarInterval.FiveMinutes => 5,
        BarInterval.FifteenMinutes => 15,
        BarInterval.OneHour => 60,
        _ => throw new ArgumentOutOfRangeException(nameof(interval), interval, "Daily bars have no fixed length in minutes."),
    };

    /// <summary>The <c>timeframe</c> code stored in <c>market.price_bars</c>.</summary>
    public static string ToTimeframe(this BarInterval interval) => interval switch
    {
        BarInterval.OneMinute => Domain.Timeframe.OneMinute,
        BarInterval.FiveMinutes => Domain.Timeframe.FiveMinutes,
        BarInterval.FifteenMinutes => Domain.Timeframe.FifteenMinutes,
        BarInterval.OneHour => Domain.Timeframe.OneHour,
        BarInterval.OneDay => Domain.Timeframe.OneDay,
        _ => throw new ArgumentOutOfRangeException(nameof(interval), interval, null),
    };
}

public sealed record MarketDataCapabilities(
    bool Realtime,
    IReadOnlyList<BarInterval> IntradayIntervals,
    bool Fundamentals,
    bool CorporateActions,
    int MaxSymbolsPerRequest);

public sealed record ProviderQuote(
    string Symbol,
    decimal Last,
    decimal? Bid,
    decimal? Ask,
    decimal Open,
    decimal High,
    decimal Low,
    decimal PrevClose,
    long Volume,
    DateTimeOffset AsOf,
    string Quality);

public sealed record ProviderBar(
    string Symbol,
    BarInterval Interval,
    DateTimeOffset BarStart,
    DateOnly SessionDate,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume,
    bool IsFinal);

public sealed record ProviderInstrument(
    string Symbol,
    string ExchangeMic,
    string Name,
    string AssetType,
    string Currency,
    string? Sector,
    string? Industry,
    decimal? MarketCap,
    string ProviderId);

public sealed record ProviderCorporateAction(string Symbol, string ActionType, DateOnly ExDate, decimal? Ratio, decimal? CashAmount);

public sealed record ProviderFundamentals(
    string Symbol,
    DateTimeOffset AsOf,
    decimal? Beta,
    decimal? PeRatio,
    decimal? EpsTtm,
    DateOnly? NextEarningsDate,
    IReadOnlyDictionary<string, string> Data);

public sealed record ProviderSession(string ExchangeMic, DateOnly SessionDate, DateTimeOffset? OpenAt, DateTimeOffset? CloseAt, bool IsHalfDay, bool IsHoliday);

public sealed record ProviderNewsItem(
    string ProviderArticleId,
    string SourceName,
    string Headline,
    string Url,
    DateTimeOffset PublishedAt,
    string? Summary,
    IReadOnlyList<string> Symbols);
