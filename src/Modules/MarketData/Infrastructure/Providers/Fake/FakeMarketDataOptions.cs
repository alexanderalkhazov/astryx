namespace Astryx.Modules.MarketData.Infrastructure.Providers.Fake;

/// <summary><c>MarketData:Fake</c> settings.</summary>
public sealed class FakeMarketDataOptions
{
    public const string SectionName = "MarketData:Fake";

    /// <summary>Same seed and same clock ⇒ identical bars, quotes and fundamentals.</summary>
    public int Seed { get; set; } = 20261109;

    /// <summary>
    /// Off: quotes show the fixture's session values (the design's NVDA 186.40 / +3.42 %). On: prices drift with a
    /// seeded per-minute random walk while the session is open.
    /// </summary>
    public bool IntradayWalk { get; set; }

    /// <summary>Daily bars generated before the current session.</summary>
    public int HistorySessions { get; set; } = 400;
}
