using Astryx.Contracts.MarketData;
using Astryx.Modules.Quant.Application.Analysis;

namespace Astryx.UnitTests.Quant;

/// <summary>Today's forming volume is projected to a full session before relative volume is compared (scanner fix).</summary>
public class FormingVolumeTests
{
    private static readonly TradingSession Session = new(
        new DateOnly(2026, 11, 17), DateTimeOffset.Parse("2026-11-17T14:30:00Z", System.Globalization.CultureInfo.InvariantCulture),
        DateTimeOffset.Parse("2026-11-17T21:00:00Z", System.Globalization.CultureInfo.InvariantCulture), false);

    [Fact]
    public void Halfway_through_the_session_volume_doubles() =>
        QuantFactsService.ProjectVolume(1_000_000, Session, Session.OpenAt + TimeSpan.FromMinutes(195L)).ShouldBe(2_000_000);

    [Fact]
    public void The_first_minutes_project_as_if_a_quarter_of_the_session_had_passed() =>
        QuantFactsService.ProjectVolume(1_000_000, Session, Session.OpenAt + TimeSpan.FromMinutes(5L)).ShouldBe(4_000_000);

    [Theory]
    [InlineData(-10L)]
    [InlineData(0L)]
    [InlineData(390L)]
    [InlineData(500L)]
    public void Outside_the_session_there_is_nothing_to_project(long minutesAfterOpen) =>
        QuantFactsService.ProjectVolume(1_000_000, Session, Session.OpenAt + TimeSpan.FromMinutes(minutesAfterOpen)).ShouldBeNull();
}
