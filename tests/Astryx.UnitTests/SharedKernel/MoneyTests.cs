using Astryx.SharedKernel.Finance;

namespace Astryx.UnitTests.SharedKernel;

public class MoneyTests
{
    [Fact]
    public void Adds_and_subtracts_same_currency() =>
        (Money.Usd(21_840.55m) - Money.Usd(14_175.52m)).ShouldBe(Money.Usd(7_665.03m)); // design: cash after the NVDA fill

    [Fact]
    public void Combining_currencies_throws()
    {
        var usd = Money.Usd(1m);
        var other = new Money(1m, (Currency)999);

        Should.Throw<CurrencyMismatchException>(() => usd + other);
        Should.Throw<CurrencyMismatchException>(() => usd > other);
    }

    [Fact]
    public void Notional_is_price_times_quantity_unrounded() =>
        Money.Of(new Price(186.52m), new Quantity(76m), Currency.USD).ShouldBe(Money.Usd(14_175.52m));

    [Fact]
    public void Comparisons_work_within_a_currency()
    {
        (Money.Usd(14_175.52m) <= Money.Usd(21_840.55m)).ShouldBeTrue();
        (Money.Usd(-630.80m) < Money.Zero(Currency.USD)).ShouldBeTrue();
        Money.Usd(-630.80m).IsNegative.ShouldBeTrue();
    }

    [Fact]
    public void Ledger_rounding_is_half_even_to_cents()
    {
        Money.Usd(2.345m).RoundForLedger().Amount.ShouldBe(2.34m);
        Money.Usd(2.355m).RoundForLedger().Amount.ShouldBe(2.36m);
        Money.Usd(-2.345m).RoundForLedger().Amount.ShouldBe(-2.34m);
    }

    [Fact]
    public void Percent_of_equity_matches_design_risk_budget() =>
        Percent.FromPercentage(0.75m).Of(Money.Usd(118_181.30m)).Amount.ShouldBe(886.35975m);
}
