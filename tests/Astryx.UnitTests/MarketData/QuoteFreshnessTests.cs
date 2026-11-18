using System.Globalization;
using Astryx.Modules.MarketData.Application.Market;

namespace Astryx.UnitTests.MarketData;

/// <summary>Quote freshness in and after the session (§8.4): a real feed's last trade lands a moment before the bell.</summary>
public class QuoteFreshnessTests
{
    private static readonly DateTimeOffset Close = DateTimeOffset.Parse("2026-11-17T21:00:00Z", CultureInfo.InvariantCulture);
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(60L);

    [Theory]
    [InlineData(-30L, -80L, false)] // 50 s old
    [InlineData(-30L, -90L, false)] // exactly the budget
    [InlineData(-30L, -95L, true)] // 65 s old
    public void In_session_a_quote_goes_stale_after_the_budget(long nowSeconds, long asOfSeconds, bool stale) =>
        FreshnessPolicy.IsQuoteStale(Close + TimeSpan.FromSeconds(asOfSeconds), Close + TimeSpan.FromSeconds(nowSeconds), Close, MaxAge).ShouldBe(stale);

    [Fact]
    public void After_the_close_the_last_trade_a_second_before_the_bell_is_the_close() =>
        FreshnessPolicy.IsQuoteStale(Close - TimeSpan.FromSeconds(1L), Close + TimeSpan.FromHours(14L), Close, MaxAge).ShouldBeFalse();

    [Fact]
    public void After_the_close_a_quote_from_midday_is_stale() =>
        FreshnessPolicy.IsQuoteStale(Close - TimeSpan.FromHours(3L), Close + TimeSpan.FromHours(1L), Close, MaxAge).ShouldBeTrue();
}
