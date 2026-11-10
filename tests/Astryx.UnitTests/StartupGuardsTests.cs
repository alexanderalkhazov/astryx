using Astryx.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace Astryx.UnitTests;

public class StartupGuardsTests
{
    private static IConfiguration Config(string? liveTrading) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(liveTrading is null ? [] : [new("Features:LiveTrading", liveTrading)])
            .Build();

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public void Allows_startup_when_live_trading_is_off(string? value) =>
        Should.NotThrow(() => StartupGuards.EnsureLiveTradingDisabled(Config(value)));

    [Fact]
    public void Refuses_startup_when_live_trading_is_on() =>
        Should.Throw<InvalidOperationException>(() => StartupGuards.EnsureLiveTradingDisabled(Config("true")));
}
