using Astryx.Contracts.MarketData;
using Astryx.Contracts.Paper;
using Astryx.Contracts.Profiles;
using Astryx.Modules.Trading.Domain.Risk;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Astryx.UnitTests.Trading;

/// <summary>The risk engine against §14.6's worked examples and the RISK-xx table (SYSTEM_DESIGN.md §14, §21, TASKS.md T-082).</summary>
public class RiskEngineTests
{
    // Monday 9 Nov 2026, 14:32 ET; the regular session runs 09:30–16:00 ET (14:30–21:00 UTC).
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 19, 32, 0, TimeSpan.Zero);
    private static readonly TradingSession Session = new(new DateOnly(2026, 11, 9), new DateTimeOffset(2026, 11, 9, 14, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 9, 21, 0, 0, TimeSpan.Zero), false);
    private static readonly RiskLimits Limits = new(0.0075m, 0.12m, 0.35m, 0.30m, 0.02m, 12, 1m, 1.5m);

    /// <summary>The design's Home values: E $118,181.30, cash $21,840.55, Semis at 12.1%, 5 open, day −0.12%.</summary>
    private static RiskInputs Design() => new(
        Symbol: "NVDA", OrderType: "buy_stop", Entry: 186.50m, Stop: 178.20m, Target: 204.00m, RiskPerTradePct: 0.0075m, RequestedQuantity: null,
        QuoteLast: 186.40m, QuoteAsOf: Now, BarsStale: false, SignalActive: true,
        Equity: 118_181.30m, CashAvailable: 21_840.55m, Sector: "Semiconductors", SectorValue: 118_181.30m * 0.121m,
        OpenPositions: 5, WorkingEntries: 0, HoldsSymbol: false, WorkingEntryForSymbol: false,
        StartOfDayEquity: 118_181.30m / (1 - 0.0012m), Atr14: 5.53m, Session: Session, Now: Now, Limits: Limits, Costs: CostModel.Default);

    private static RiskCheck Check(RiskResult r, string code) => r.Checks.Single(c => c.Code == code);

    [Fact]
    public void RISK_01_the_design_proposal_sizes_to_76_shares_by_the_weight_cap()
    {
        var r = RiskEngine.Assess(Design());

        (r.QuantityByRisk, r.QuantityByWeight, r.QuantityByCash).ShouldBe((106m, 76m, 117m));
        (r.Quantity, r.SizedBy).ShouldBe((76m, "weight_cap"));
        (r.CapitalRequired, r.MaxLoss, r.PotentialProfit).ShouldBe((14_174.00m, 630.80m, 1_330.00m));
        Math.Round(r.RewardRisk, 2, MidpointRounding.ToEven).ShouldBe(2.11m);
        (r.SlippagePerShare, r.EstSlippage, r.MaxLossInclCosts).ShouldBe((0.02m, 1.52m, 633.84m));
        Math.Round(r.CashAfter, 2).ShouldBe(7_665.03m);
        r.Approvable.ShouldBeTrue();
        Check(r, "position_weight").ShouldSatisfyAllConditions(c => c.Status.ShouldBe("warn"), c => c.Message.ShouldBe("Weight 11.99%, close to 12% cap"));
        Check(r, "sector_exposure").Message.ShouldBe("Semiconductors 24.1% ≤ 35%");
        Check(r, "cash_available").Message.ShouldBe("$14,175.52 of $21,840.55 cash");
        Check(r, "daily_loss_halt").Message.ShouldBe("Today −0.12% vs −2.00% halt");
        Check(r, "session_open").Message.ShouldBe("Regular session open until 16:00 ET");
        r.Checks.Where(c => c.Code is not "position_weight").ShouldAllBe(c => c.Status == "pass");
        r.Checks.Select(c => c.Code).Take(6).ShouldBe(RiskEngine.DesignChecks);
    }

    [Fact]
    public void A_simulated_portfolio_has_no_broker_checks() =>
        RiskEngine.Assess(Design()).Checks.ShouldNotContain(c => c.Code.StartsWith("broker_", StringComparison.Ordinal));

    [Fact]
    public void A_ready_Alpaca_portfolio_passes_the_three_broker_checks()
    {
        var r = RiskEngine.Assess(Design() with { Broker = new BrokerReadiness(Connected: true, CanPlaceOrders: true, Eligible: true) });

        r.Checks.Where(c => c.Code.StartsWith("broker_", StringComparison.Ordinal)).Select(c => (c.Code, c.Status))
            .ShouldBe([("broker_connected", "pass"), ("broker_write_permission", "pass"), ("broker_eligible", "pass")]);
        r.Approvable.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false, false, null, "broker_connected", "fail")]
    [InlineData(true, false, true, "broker_write_permission", "fail")]
    [InlineData(true, true, false, "broker_eligible", "fail")]
    [InlineData(true, true, null, "broker_eligible", "warn")] // Alpaca unreachable: its answer at submit decides
    public void Broker_readiness_gates_approval(bool connected, bool canPlace, bool? eligible, string code, string status)
    {
        var r = RiskEngine.Assess(Design() with { Broker = new BrokerReadiness(connected, canPlace, eligible, eligible == false ? "Not tradable at Alpaca." : null) });

        Check(r, code).Status.ShouldBe(status);
        r.Approvable.ShouldBe(status != "fail");
        if (eligible == false)
        {
            Check(r, code).Message.ShouldBe("Not tradable at Alpaca.");
        }
    }

    [Fact]
    public void RISK_02_eighty_shares_break_the_weight_cap()
    {
        var r = RiskEngine.Assess(Design() with { RequestedQuantity = 80 });

        (r.SizedBy, r.Approvable).ShouldBe(("user", false));
        Check(r, "position_weight").ShouldSatisfyAllConditions(c => c.Status.ShouldBe("fail"), c => c.Message.ShouldBe("Position weight 12.62% exceeds 12% cap. Max 76 shares."));
    }

    [Theory]
    [InlineData("0.98", "fail")] // −2.00% exactly: the boundary halts (RISK-03)
    [InlineData("0.9795", "fail")]
    [InlineData("0.984", "warn")] // −1.60%, past 0.75 × the halt
    [InlineData("0.999", "pass")]
    public void RISK_03_the_daily_loss_halt_is_inclusive(string ratio, string expected)
    {
        var r = RiskEngine.Assess(Design() with { StartOfDayEquity = 118_181.30m / decimal.Parse(ratio, System.Globalization.CultureInfo.InvariantCulture) });

        Check(r, "daily_loss_halt").Status.ShouldBe(expected);
    }

    [Fact]
    public void R4_message_for_a_halted_day()
    {
        var r = RiskEngine.Assess(Design() with { StartOfDayEquity = 118_181.30m / 0.9795m });

        Check(r, "daily_loss_halt").Message.ShouldBe("Daily loss halt reached (−2.05% ≤ −2.00%). New entries paused until next session.");
        r.Approvable.ShouldBeFalse();
    }

    [Fact]
    public void RISK_04_twelve_open_positions_block_a_thirteenth()
    {
        var r = RiskEngine.Assess(Design() with { OpenPositions = 12 });

        Check(r, "max_open_positions").ShouldSatisfyAllConditions(c => c.Status.ShouldBe("fail"), c => c.Message.ShouldBe("12 of 12 positions open."));
        Check(RiskEngine.Assess(Design() with { OpenPositions = 10, WorkingEntries = 1 }), "max_open_positions").Status.ShouldBe("warn");
    }

    [Fact]
    public void R2_R3_bad_levels_fail_levels_valid_or_reward_risk()
    {
        var close = RiskEngine.Assess(Design() with { Stop = 185.00m });
        Check(close, "levels_valid").Message.ShouldBe("Stop is too close (0.27× ATR). Minimum 0.5× ATR.");

        var poor = RiskEngine.Assess(Design() with { Target = 190.00m });
        Check(poor, "reward_risk").ShouldSatisfyAllConditions(c => c.Status.ShouldBe("fail"), c => c.Message.ShouldBe("Reward:risk 0.42 below minimum 1.50"));

        var inverted = RiskEngine.Assess(Design() with { Stop = 190m });
        (Check(inverted, "levels_valid").Status, inverted.Approvable, inverted.Quantity).ShouldBe(("fail", false, 0m));
    }

    [Fact]
    public void R5_sector_exposure_beyond_35_percent_fails()
    {
        var r = RiskEngine.Assess(Design() with { SectorValue = 118_181.30m * 0.30m });

        Check(r, "sector_exposure").ShouldSatisfyAllConditions(c => c.Status.ShouldBe("fail"), c => c.Message.ShouldBe("Semiconductors would be 42.0% (limit 35%)."));
    }

    [Fact]
    public void R7_holding_the_symbol_is_a_duplicate()
    {
        Check(RiskEngine.Assess(Design() with { HoldsSymbol = true }), "no_duplicate").Message.ShouldBe("You already hold NVDA.");
        Check(RiskEngine.Assess(Design() with { WorkingEntryForSymbol = true }), "no_duplicate").Status.ShouldBe("fail");
    }

    [Theory]
    [InlineData(20, 58, "fail", "Regular session closes in 2 min. Approvals stop 5 min before close.")] // R8 15:58 ET
    [InlineData(20, 56, "fail", "Regular session closes in 4 min. Approvals stop 5 min before close.")] // RISK-09 15:56 ET
    [InlineData(20, 40, "warn", "Regular session closes in 20 min.")]
    [InlineData(21, 0, "fail", "The regular session is closed. Approvals resume at the next open.")]
    [InlineData(14, 29, "fail", "The regular session is closed. Approvals resume at the next open.")]
    public void R8_RISK_09_approvals_stop_five_minutes_before_the_close(int hour, int minute, string status, string message)
    {
        var at = new DateTimeOffset(2026, 11, 9, hour, minute, 0, TimeSpan.Zero);

        var r = RiskEngine.Assess(Design() with { Now = at, QuoteAsOf = at });

        Check(r, "session_open").ShouldSatisfyAllConditions(c => c.Status.ShouldBe(status), c => c.Message.ShouldBe(message));
    }

    [Fact]
    public void RISK_08_R9_a_quote_older_than_90_seconds_is_stale()
    {
        Check(RiskEngine.Assess(Design() with { QuoteAsOf = Now.AddSeconds(-90) }), "data_fresh").Status.ShouldBe("pass");
        var stale = RiskEngine.Assess(Design() with { QuoteAsOf = Now.AddSeconds(-91) });
        Check(stale, "data_fresh").Status.ShouldBe("fail");
        Check(RiskEngine.Assess(Design() with { QuoteAsOf = Now.AddMinutes(-20) }), "data_fresh").Message.ShouldBe("Prices are stale (last 14:12 ET). Approvals disabled.");
        Check(RiskEngine.Assess(Design() with { SignalActive = false }), "data_fresh").Status.ShouldBe("fail");
    }

    [Fact]
    public void R10_little_cash_sizes_by_cash_and_forcing_76_fails()
    {
        var sized = RiskEngine.Assess(Design() with { CashAvailable = 9_000m });
        (sized.Quantity, sized.SizedBy, sized.Approvable).ShouldBe((48m, "cash", true));

        var forced = RiskEngine.Assess(Design() with { CashAvailable = 9_000m, RequestedQuantity = 76 });
        Check(forced, "cash_available").Message.ShouldBe("Needs $14,175.52, $9,000.00 available.");
        Check(forced, "quantity_valid").Status.ShouldBe("fail");
    }

    [Fact]
    public void Risk_above_the_users_limit_fails_and_costs_are_counted()
    {
        Check(RiskEngine.Assess(Design() with { RiskPerTradePct = 0.01m }), "risk_per_trade").Status.ShouldBe("fail");

        // 106 shares by risk alone: with the weight cap lifted, costs push the real risk just over 0.75%.
        var r = RiskEngine.Assess(Design() with { Limits = Limits with { MaxPositionWeightPct = 0.25m } });
        (r.Quantity, r.SizedBy).ShouldBe((106m, "risk"));
        r.MaxLossInclCosts.ShouldBe(106m * (8.30m + 0.04m)); // 884.04: 0.748% of equity, still under 0.75%
        Check(r, "risk_per_trade").Status.ShouldBe("pass");
        Check(RiskEngine.Assess(Design() with { RequestedQuantity = 0 }), "quantity_valid").Status.ShouldBe("fail");
    }

    public static Arbitrary<RiskInputs> AnyInputs() =>
        (from equity in Gen.Choose(5_000, 2_000_000)
         from cashPct in Gen.Choose(0, 100)
         from entryCents in Gen.Choose(500, 200_000)
         from stopBp in Gen.Choose(50, 2_000)
         from targetBp in Gen.Choose(50, 5_000)
         from riskBp in Gen.Elements(50, 75, 100)
         from weightPct in Gen.Choose(2, 25)
         from open in Gen.Choose(0, 14)
         select Design() with
         {
             Equity = equity,
             CashAvailable = equity * cashPct / 100m,
             Entry = entryCents / 100m,
             Stop = Math.Round(entryCents / 100m * (1 - (stopBp / 10_000m)), 2),
             Target = Math.Round(entryCents / 100m * (1 + (targetBp / 10_000m)), 2),
             QuoteLast = entryCents / 100m,
             RiskPerTradePct = riskBp / 10_000m,
             SectorValue = 0,
             OpenPositions = open,
             Atr14 = null,
             Limits = Limits with { MaxRiskPerTradePct = 0.01m, MaxPositionWeightPct = weightPct / 100m },
         }).ToArbitrary();

    [Property(Arbitrary = [typeof(RiskEngineTests)], MaxTest = 500)]
    public bool RISK_10_default_sizing_never_exceeds_any_bound(RiskInputs inputs)
    {
        var r = RiskEngine.Assess(inputs);
        var withinBounds = r.Quantity <= r.QuantityByRisk && r.Quantity <= r.QuantityByWeight && r.Quantity <= r.QuantityByCash;
        var gateConsistent = !r.Approvable || r.Checks.All(c => c.Status is "pass" or "warn");
        var neverOverCash = r.Quantity == 0 || r.CapitalRequired + r.EstSlippage + r.EstFees / 2 <= inputs.CashAvailable;
        var neverOverWeight = r.CapitalRequired <= inputs.Equity * inputs.Limits.MaxPositionWeightPct;
        return withinBounds && gateConsistent && neverOverCash && neverOverWeight;
    }
}
