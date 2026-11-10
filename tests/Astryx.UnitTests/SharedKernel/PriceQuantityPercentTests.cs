using Astryx.SharedKernel.Finance;

namespace Astryx.UnitTests.SharedKernel;

public class PriceQuantityPercentTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    public void Price_must_be_positive(decimal value)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new Price(value));
        Price.TryCreate(value, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData(186.505, 186.50)]
    [InlineData(186.515, 186.52)]
    [InlineData(0.123456, 0.1235)]
    public void Price_rounds_to_tick_half_even(decimal raw, decimal expected) =>
        new Price(raw).RoundToTick().Value.ShouldBe(expected);

    [Fact]
    public void Quantity_cannot_be_negative() =>
        Should.Throw<ArgumentOutOfRangeException>(() => new Quantity(-1m));

    [Theory]
    [InlineData(76.04, 76)]   // design: weight-cap bound 14,181.76 / 186.50
    [InlineData(106.79, 106)] // design: risk-budget bound 886.36 / 8.30
    [InlineData(-3, 0)]
    public void Sizing_floors_to_whole_shares(decimal raw, decimal expected) =>
        Quantity.FloorOf(raw).Value.ShouldBe(expected);

    [Fact]
    public void Quantity_min_and_wholeness()
    {
        Quantity.Min(new Quantity(106m), new Quantity(76m)).ShouldBe(new Quantity(76m));
        new Quantity(76m).IsWhole.ShouldBeTrue();
        new Quantity(76.5m).IsWhole.ShouldBeFalse();
    }

    [Fact]
    public void Percent_is_stored_as_fraction()
    {
        var risk = Percent.FromPercentage(0.75m);
        risk.Fraction.ShouldBe(0.0075m);
        risk.AsPercentage.ShouldBe(0.75m);
        (risk <= new Percent(0.01m)).ShouldBeTrue();
    }

    [Theory]
    [InlineData(0.01865, 186.50, 0.02)]   // design: 1 bp slippage on 186.50 → $0.02
    [InlineData(0.01782, 178.20, 0.02)]   // 1 bp on the 178.20 stop
    [InlineData(0.02, 186.50, 0.02)]
    [InlineData(0.00005, 0.50, 0.0001)]   // sub-dollar stock uses the $0.0001 tick
    public void Slippage_rounds_up_to_the_price_tick(decimal raw, decimal price, decimal expected) =>
        RoundingPolicy.CeilingToTick(raw, price).ShouldBe(expected);
}
