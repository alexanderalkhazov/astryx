using System.Globalization;
using Astryx.Contracts.MarketData;
using Astryx.Contracts.Paper;
using Astryx.Contracts.Profiles;
using Astryx.Contracts.Strategies;

namespace Astryx.Modules.Trading.Domain.Risk;

/// <summary>Paper cost model (§15): slippage per share max(MinSlippage, ceil_cent(P × bps/10000)) and a flat fee per order.</summary>
/// <param name="HalfSpread">Set for an Alpaca portfolio with a usable quote: crossing costs half the quoted spread per share.</param>
public sealed record CostModel(int SlippageBps, decimal MinSlippage, decimal FeePerOrder, decimal? HalfSpread = null)
{
    public static readonly CostModel Default = new(1, 0.01m, 0m);

    /// <summary>
    /// Alpaca (§17.3, T-146): commission-free, and a marketable order pays half the quoted spread. Without a usable spread
    /// the basis-point slippage of <paramref name="fallback"/> applies. Regulatory fees (SEC, FINRA TAF, CAT) are charged
    /// on live sells only and are not part of the estimate; Alpaca Paper doesn't charge them (Q-23).
    /// </summary>
    public static CostModel Alpaca(decimal? bid, decimal? ask, CostModel fallback) =>
        new(fallback.SlippageBps, fallback.MinSlippage, 0m, bid is > 0 && ask is { } a && a >= bid ? (a - bid.Value) / 2 : null);

    public decimal SlippagePerShare(decimal price) => HalfSpread is { } half
        ? Math.Max(MinSlippage, Math.Ceiling(half * 100m) / 100m)
        : Math.Max(MinSlippage, Math.Ceiling(price * SlippageBps / 10_000m * 100m) / 100m);
}

/// <summary>Everything one assessment depends on (§14.2, §14.5). Money in USD at full precision; fractions as fractions.</summary>
public sealed record RiskInputs(
    string Symbol,
    string OrderType,
    decimal Entry,
    decimal Stop,
    decimal Target,
    decimal RiskPerTradePct,
    decimal? RequestedQuantity,
    decimal QuoteLast,
    DateTimeOffset QuoteAsOf,
    bool BarsStale,
    bool SignalActive,
    decimal Equity,
    decimal CashAvailable,
    string? Sector,
    decimal SectorValue,
    int OpenPositions,
    int WorkingEntries,
    bool HoldsSymbol,
    bool WorkingEntryForSymbol,
    decimal? StartOfDayEquity,
    decimal? Atr14,
    TradingSession? Session,
    DateTimeOffset Now,
    RiskLimits Limits,
    CostModel Costs,
    BrokerReadiness? Broker = null,
    StrategyEvidence? Evidence = null);

public static class CheckStatus
{
    public const string Pass = "pass";
    public const string Warn = "warn";
    public const string Fail = "fail";
}

/// <summary>One pre-trade check (§14.5). <paramref name="Actual"/> and <paramref name="Limit"/> are unrounded.</summary>
public sealed record RiskCheck(string Code, string Status, string Message, decimal? Actual = null, decimal? Limit = null);

/// <summary>The engine's verdict: sizing, the derived money figures and every check. Approvable ⇔ no check fails.</summary>
public sealed record RiskResult(
    decimal Quantity, string SizedBy, decimal QuantityByRisk, decimal QuantityByWeight, decimal QuantityByCash,
    decimal SlippagePerShare, decimal CapitalRequired, decimal MaxLoss, decimal MaxLossInclCosts, decimal PotentialProfit, decimal RewardRisk,
    decimal EstFees, decimal EstSlippage, decimal RiskPct, decimal NewWeight, decimal SectorAfter, decimal CashAfter, int OpenPositionsAfter,
    IReadOnlyList<RiskCheck> Checks)
{
    public bool Approvable => Checks.All(c => c.Status != CheckStatus.Fail);
}

/// <summary>
/// The authoritative gate between research and execution (SYSTEM_DESIGN.md §14): deterministic, side-effect free and
/// with no override. It sizes a long position by the smallest of the risk, weight-cap and cash bounds (or takes the
/// user's quantity) and runs the twelve pre-trade checks. The six design checks are always reported; the engine
/// checks always run, and the client shows them only when they warn or fail.
/// </summary>
public static class RiskEngine
{
    public static readonly TimeSpan MaxQuoteAge = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan ApprovalCutoff = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan CloseWarning = TimeSpan.FromMinutes(30);

    public static readonly IReadOnlyList<string> DesignChecks = ["risk_per_trade", "position_weight", "sector_exposure", "cash_available", "daily_loss_halt", "session_open"];

    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    public static RiskResult Assess(RiskInputs i)
    {
        var limits = i.Limits;
        var slip = i.Costs.SlippagePerShare(i.Entry);
        var fee = i.Costs.FeePerOrder;
        var rShare = i.Entry - i.Stop;
        var wShare = i.Target - i.Entry;
        var levelsOrdered = rShare > 0 && wShare > 0;

        var qRisk = levelsOrdered ? Floor(i.Equity * i.RiskPerTradePct / rShare) : 0m;
        var qWeight = i.Entry > 0 ? Floor(i.Equity * limits.MaxPositionWeightPct / i.Entry) : 0m;
        var qCash = Floor((i.CashAvailable - fee) / (i.Entry + slip));
        decimal q;
        string sizedBy;
        if (i.RequestedQuantity is { } requested)
        {
            (q, sizedBy) = (requested, "user");
        }
        else
        {
            // Tie order: risk, weight_cap, cash (§14.4).
            (q, sizedBy) = qRisk <= qWeight && qRisk <= qCash ? (qRisk, "risk") : qWeight <= qCash ? (qWeight, "weight_cap") : (qCash, "cash");
        }

        var capital = q * i.Entry;
        var maxLoss = q * rShare;
        var maxLossInclCosts = (q * (rShare + (2 * slip))) + (2 * fee);
        var profit = q * wShare;
        var rewardRisk = levelsOrdered ? wShare / rShare : 0m;
        var estSlippage = q * slip;
        var cashNeeded = capital + estSlippage + fee;
        var equity = i.Equity;
        var riskPct = equity > 0 ? maxLossInclCosts / equity : 1m;
        var newWeight = equity > 0 ? capital / equity : 1m;
        var sectorAfter = equity > 0 ? (i.SectorValue + capital) / equity : 1m;
        var openAfter = i.OpenPositions + i.WorkingEntries + 1;

        var checks = new List<RiskCheck>
        {
            RiskPerTrade(riskPct, i.RiskPerTradePct, limits.MaxRiskPerTradePct),
            PositionWeight(newWeight, limits.MaxPositionWeightPct, qWeight),
            SectorExposure(i.Sector ?? "Unclassified", sectorAfter, limits.MaxSectorExposurePct, limits.SectorWarningPct),
            CashAvailable(cashNeeded, i.CashAvailable),
            DailyLossHalt(i.Equity, i.StartOfDayEquity, limits.DailyLossHaltPct),
            SessionOpen(i.Session, i.Now),
            MaxOpenPositions(openAfter, limits.MaxOpenPositions),
            LevelsValid(i, rShare),
            RewardRiskCheck(rewardRisk, levelsOrdered, limits.MinRewardRisk),
            NoDuplicate(i),
            DataFresh(i),
            QuantityValid(q, qCash),
        };
        if (i.Broker is { } broker)
        {
            checks.AddRange(BrokerChecks(broker));
        }

        if (i.Evidence is { } evidence)
        {
            checks.Add(EvidenceCheck(evidence));
        }

        return new RiskResult(
            q, sizedBy, qRisk, qWeight, qCash, slip, capital, maxLoss, maxLossInclCosts, profit, rewardRisk, 2 * fee, estSlippage,
            riskPct, newWeight, sectorAfter, i.CashAvailable - cashNeeded, openAfter, checks);
    }

    private static RiskCheck RiskPerTrade(decimal riskPct, decimal r, decimal max)
    {
        var limit = Math.Min(r, max);
        if (r > max)
        {
            return new("risk_per_trade", CheckStatus.Fail, $"Risk per trade {Pct(r)} is above your {Pct(max)} limit.", r, max);
        }

        return riskPct <= limit
            ? new("risk_per_trade", CheckStatus.Pass, $"Risk {Pct(riskPct)} ≤ {Pct(limit)} limit", riskPct, limit)
            : new("risk_per_trade", CheckStatus.Fail, $"Risk {Pct(riskPct)} including costs exceeds the {Pct(limit)} limit. Reduce the quantity.", riskPct, limit);
    }

    private static RiskCheck PositionWeight(decimal weight, decimal max, decimal qWeight) =>
        weight > max ? new("position_weight", CheckStatus.Fail, $"Position weight {Pct(weight)} exceeds {Pct(max, 0)} cap. Max {qWeight.ToString("0", CultureInfo.InvariantCulture)} shares.", weight, max)
        : weight > 0.9m * max ? new("position_weight", CheckStatus.Warn, $"Weight {Pct(weight)}, close to {Pct(max, 0)} cap", weight, max)
        : new("position_weight", CheckStatus.Pass, $"Weight {Pct(weight)} ≤ {Pct(max, 0)} cap", weight, max);

    private static RiskCheck SectorExposure(string sector, decimal after, decimal max, decimal warn) =>
        after > max ? new("sector_exposure", CheckStatus.Fail, $"{sector} would be {Pct(after, 1)} (limit {Pct(max, 0)}).", after, max)
        : after > warn ? new("sector_exposure", CheckStatus.Warn, $"{sector} {Pct(after, 1)}, above the {Pct(warn, 0)} warning line", after, max)
        : new("sector_exposure", CheckStatus.Pass, $"{sector} {Pct(after, 1)} ≤ {Pct(max, 0)}", after, max);

    private static RiskCheck CashAvailable(decimal needed, decimal cash) =>
        needed > cash ? new("cash_available", CheckStatus.Fail, $"Needs {Money(needed)}, {Money(cash)} available.", needed, cash)
        : needed > 0.9m * cash ? new("cash_available", CheckStatus.Warn, $"{Money(needed)} of {Money(cash)} cash, over 90% of it", needed, cash)
        : new("cash_available", CheckStatus.Pass, $"{Money(needed)} of {Money(cash)} cash", needed, cash);

    private static RiskCheck DailyLossHalt(decimal equity, decimal? startOfDay, decimal halt)
    {
        var day = startOfDay is > 0 and { } sod ? (equity - sod) / sod : 0m;
        return day <= -halt ? new("daily_loss_halt", CheckStatus.Fail, $"Daily loss halt reached ({Pct(day, 2, signed: true)} ≤ {Pct(-halt, 2, signed: true)}). New entries paused until next session.", day, -halt)
            : day <= -0.75m * halt ? new("daily_loss_halt", CheckStatus.Warn, $"Today {Pct(day, 2, signed: true)}, close to the {Pct(-halt, 2, signed: true)} halt", day, -halt)
            : new("daily_loss_halt", CheckStatus.Pass, $"Today {Pct(day, 2, signed: true)} vs {Pct(-halt, 2, signed: true)} halt", day, -halt);
    }

    private static RiskCheck SessionOpen(TradingSession? session, DateTimeOffset now)
    {
        if (session is null || now < session.OpenAt || now >= session.CloseAt)
        {
            return new("session_open", CheckStatus.Fail, "The regular session is closed. Approvals resume at the next open.");
        }

        var left = session.CloseAt - now;
        if (left < ApprovalCutoff)
        {
            return new("session_open", CheckStatus.Fail, $"Regular session closes in {Minutes(left)} min. Approvals stop 5 min before close.");
        }

        return left < CloseWarning
            ? new("session_open", CheckStatus.Warn, $"Regular session closes in {Minutes(left)} min.")
            : new("session_open", CheckStatus.Pass, $"Regular session open until {Et(session.CloseAt)} ET");
    }

    private static RiskCheck MaxOpenPositions(int after, int max) =>
        after > max ? new("max_open_positions", CheckStatus.Fail, $"{after - 1} of {max} positions open.", after, max)
        : after == max ? new("max_open_positions", CheckStatus.Warn, $"This would be position {after} of {max}.", after, max)
        : new("max_open_positions", CheckStatus.Pass, $"{after} of {max} positions after this trade", after, max);

    private static RiskCheck LevelsValid(RiskInputs i, decimal rShare)
    {
        if (!(i.Stop < i.Entry && i.Entry < i.Target))
        {
            return new("levels_valid", CheckStatus.Fail, "Levels must satisfy stop < entry < target for a long.");
        }

        if (i.OrderType == "buy_stop" && i.Entry < i.QuoteLast * 0.995m || i.OrderType == "limit" && i.Entry > i.QuoteLast * 1.005m
            || Math.Abs(i.Entry - i.QuoteLast) > i.QuoteLast * 0.03m)
        {
            return new("levels_valid", CheckStatus.Fail, $"Entry {Price(i.Entry)} is too far from the last price {Price(i.QuoteLast)} for a {i.OrderType.Replace('_', ' ')} order.", i.Entry, i.QuoteLast);
        }

        if (i.Atr14 is not > 0 || i.Atr14 is not { } atr)
        {
            return new("levels_valid", CheckStatus.Pass, "Levels ordered; ATR unavailable for the distance check");
        }

        var multiple = rShare / atr;
        return multiple < 0.5m ? new("levels_valid", CheckStatus.Fail, $"Stop is too close ({Ratio(multiple)}× ATR). Minimum 0.5× ATR.", multiple, 0.5m)
            : multiple > 4m ? new("levels_valid", CheckStatus.Fail, $"Stop is too wide ({Ratio(multiple)}× ATR). Maximum 4× ATR.", multiple, 4m)
            : multiple < 1m ? new("levels_valid", CheckStatus.Warn, $"Stop is tight ({Ratio(multiple)}× ATR).", multiple, 1m)
            : new("levels_valid", CheckStatus.Pass, $"Stop {Ratio(multiple)}× ATR", multiple, 4m);
    }

    private static RiskCheck RewardRiskCheck(decimal rr, bool ordered, decimal min) =>
        !ordered || rr < min ? new("reward_risk", CheckStatus.Fail, $"Reward:risk {Ratio(rr)} below minimum {Ratio(min)}", rr, min)
        : rr < min + 0.25m ? new("reward_risk", CheckStatus.Warn, $"Reward:risk {Ratio(rr)}, close to the {Ratio(min)} minimum", rr, min)
        : new("reward_risk", CheckStatus.Pass, $"Reward:risk {Ratio(rr)} ≥ {Ratio(min)}", rr, min);

    /// <summary>
    /// What the proposal's strategy did in its real backtest (T-166). It never blocks a trade: the user decides, but
    /// sees plainly when the strategy lost money, lagged simply holding the stocks, or hasn't been tested yet.
    /// </summary>
    private static RiskCheck EvidenceCheck(StrategyEvidence e) => e.Verdict switch
    {
        StrategyEvidence.Losing => new("strategy_evidence", CheckStatus.Warn, $"This strategy lost money in its backtest ({Pct(e.Cagr!.Value, 1, signed: true)} a year).", e.Cagr, 0m),
        StrategyEvidence.LagsMarket => new("strategy_evidence", CheckStatus.Warn,
            $"In its backtest this strategy made {Pct(e.Cagr!.Value, 1)} a year, less than simply holding the stocks (×{Ratio(e.Multiple!.Value)} vs ×{Ratio(e.BenchmarkMultiple!.Value)}).", e.Multiple, e.BenchmarkMultiple),
        StrategyEvidence.BeatsMarket => new("strategy_evidence", CheckStatus.Pass, $"Beat simply holding the stocks in its backtest (×{Ratio(e.Multiple!.Value)})."),
        _ => new("strategy_evidence", CheckStatus.Warn, "This strategy hasn't been backtested on real data yet."),
    };

    /// <summary>Broker portfolios only (§17.3, T-142): the connection, the account's permission to trade, and the asset.</summary>
    private static IEnumerable<RiskCheck> BrokerChecks(BrokerReadiness b)
    {
        yield return b.Connected
            ? new("broker_connected", CheckStatus.Pass, "Alpaca connected")
            : new("broker_connected", CheckStatus.Fail, "The Alpaca connection is lost. Reconnect in Settings.");
        yield return b.CanPlaceOrders
            ? new("broker_write_permission", CheckStatus.Pass, "Alpaca account can place orders")
            : new("broker_write_permission", CheckStatus.Fail, b.Connected ? "Alpaca has blocked trading on this account." : "Orders can't be placed without an Alpaca connection.");
        yield return b.Eligible switch
        {
            true => new("broker_eligible", CheckStatus.Pass, "Tradable at Alpaca"),
            false => new("broker_eligible", CheckStatus.Fail, b.Reason ?? "Not tradable at Alpaca."),
            null when b.Connected => new("broker_eligible", CheckStatus.Warn, b.Reason ?? "Alpaca could not confirm this symbol is tradable."),
            null => new("broker_eligible", CheckStatus.Fail, "Tradability can't be checked without an Alpaca connection."),
        };
    }

    private static RiskCheck NoDuplicate(RiskInputs i) =>
        i.HoldsSymbol ? new("no_duplicate", CheckStatus.Fail, $"You already hold {i.Symbol}.")
        : i.WorkingEntryForSymbol ? new("no_duplicate", CheckStatus.Fail, $"A {i.Symbol} entry order is already working.")
        : new("no_duplicate", CheckStatus.Pass, $"No open {i.Symbol} position or order");

    private static RiskCheck DataFresh(RiskInputs i)
    {
        var age = i.Now - i.QuoteAsOf;
        if (age > MaxQuoteAge)
        {
            return new("data_fresh", CheckStatus.Fail, $"Prices are stale (last {Et(i.QuoteAsOf)} ET). Approvals disabled.", age.Ticks / TimeSpan.TicksPerSecond, MaxQuoteAge.Ticks / TimeSpan.TicksPerSecond);
        }

        return i.BarsStale ? new("data_fresh", CheckStatus.Fail, "Daily bars are stale. Approvals disabled.")
            : !i.SignalActive ? new("data_fresh", CheckStatus.Fail, "The signal behind this proposal is no longer active.")
            : new("data_fresh", CheckStatus.Pass, $"Quote from {Et(i.QuoteAsOf)} ET");
    }

    private static RiskCheck QuantityValid(decimal q, decimal qCash) =>
        q < 1 || q != Math.Floor(q) ? new("quantity_valid", CheckStatus.Fail, "Quantity must be a whole number of at least 1 share.", q, 1)
        : q > qCash ? new("quantity_valid", CheckStatus.Fail, $"{Shares(q)} shares need more cash than available (max {Shares(qCash)}).", q, qCash)
        : new("quantity_valid", CheckStatus.Pass, $"{Shares(q)} shares", q, qCash);

    private static decimal Floor(decimal value) => value <= 0 ? 0 : Math.Floor(value);

    private static string Pct(decimal fraction, int places = 2, bool signed = false)
    {
        var value = Math.Round(fraction * 100, places, MidpointRounding.ToEven);
        var text = Math.Abs(value).ToString("F" + places.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) + "%";
        return value < 0 ? "−" + text : signed && value > 0 ? "+" + text : text;
    }

    private static string Money(decimal value) => "$" + Math.Round(value, 2, MidpointRounding.ToEven).ToString("N2", CultureInfo.InvariantCulture);

    private static string Price(decimal value) => Math.Round(value, 2, MidpointRounding.ToEven).ToString("F2", CultureInfo.InvariantCulture);

    private static string Ratio(decimal value) => Math.Round(value, 2, MidpointRounding.ToEven).ToString("0.00", CultureInfo.InvariantCulture);

    private static string Shares(decimal value) => value.ToString("0", CultureInfo.InvariantCulture);

    private static string Minutes(TimeSpan span) => ((span.Ticks + TimeSpan.TicksPerMinute - 1) / TimeSpan.TicksPerMinute).ToString(CultureInfo.InvariantCulture);

    private static string Et(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, NewYork).ToString("HH:mm", CultureInfo.InvariantCulture);
}
