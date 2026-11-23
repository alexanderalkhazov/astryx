using Astryx.Contracts.Strategies;
using Astryx.Modules.Strategies.Application;
using Astryx.Modules.Strategies.Domain;

namespace Astryx.UnitTests.Strategies;

/// <summary>The backtest verdict behind the evidence gate (TASKS.md T-166).</summary>
public class StrategyEvidenceTests
{
    private static BacktestRun Run(string assumptions, string metrics) => new()
    {
        Status = RunStatus.Completed,
        Assumptions = assumptions,
        Metrics = metrics,
        EquityCurve = "[]",
        DrawdownCurve = "[]",
        MonthlyReturns = "[]",
        PeriodStart = new DateOnly(2022, 11, 25),
        PeriodEnd = new DateOnly(2026, 11, 20),
    };

    private static string Real(string benchmark = "1.86") => "{\"illustrative\": false, \"benchmark\": {\"name\": \"holding every stock equally\", \"multiple\": \"" + benchmark + "\"}}";

    private static string Full(string cagr, string multiple) => "{\"full\": {\"cagr\": \"" + cagr + "\", \"multiple\": \"" + multiple + "\"}}";

    [Theory]
    [InlineData("-0.030", "0.89", StrategyEvidence.Losing)]
    [InlineData("0.000", "1.00", StrategyEvidence.Losing)]
    [InlineData("0.062", "1.27", StrategyEvidence.LagsMarket)]
    [InlineData("0.190", "2.01", StrategyEvidence.BeatsMarket)]
    public void A_real_run_is_judged_on_its_return_and_against_holding_the_stocks(string cagr, string multiple, string verdict)
    {
        var evidence = StrategyEvidenceSource.Evidence(Run(Real(), Full(cagr, multiple)));

        evidence.Verdict.ShouldBe(verdict);
        (evidence.BenchmarkMultiple, evidence.PeriodEnd).ShouldBe((1.86m, new DateOnly(2026, 11, 20)));
    }

    [Fact]
    public void Seeded_illustrative_runs_are_not_evidence()
    {
        StrategyEvidenceSource.IsReal(Run("""{"universe": "illustrative"}""", Full("0.169", "6.02"))).ShouldBeFalse();
        StrategyEvidenceSource.IsReal(Run(Real(), Full("0.169", "6.02"))).ShouldBeTrue();
    }
}
