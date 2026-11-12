#!/usr/bin/env python3
"""Seed data for the strategy catalog (SYSTEM_DESIGN.md §7.8, TASKS.md T-063).

Writes src/Modules/Strategies/Infrastructure/Seed/catalog.json: the five design strategies with their parameter
schemas, version history and one seeded backtest run each. Values shown in the designs (Strategy lab, comparison,
backtest and configuration boards) are copied; values the designs do not show are marked ASSUMPTION in
SYSTEM_DESIGN.md and are illustrative.

Equity curves are monthly, Jan 2015 – Jun 2026, generated deterministically so that each ends at the design's
growth multiple (×6.02 …) and its maximum drawdown matches the design's figure. Backtest results are illustrative of
history, never a forecast.

Standard library only. Run manually and commit the output:

    python3 tools/gen_strategy_seed.py
"""

from __future__ import annotations

import json
import math
import random
from pathlib import Path

OUT = Path(__file__).resolve().parent.parent / "src" / "Modules" / "Strategies" / "Infrastructure" / "Seed" / "catalog.json"
START_YEAR, START_MONTH = 2015, 1
MONTHS = 138  # Jan 2015 … Jun 2026
OOS_START = "2023-01-01"
CAPITAL = 100_000


def months():
    year, month = START_YEAR, START_MONTH
    for _ in range(MONTHS):
        yield f"{year:04d}-{month:02d}-01"
        month += 1
        if month == 13:
            year, month = year + 1, 1


def max_drawdown(values):
    peak, worst = values[0], 0.0
    for v in values:
        peak = max(peak, v)
        worst = min(worst, v / peak - 1)
    return worst


def curve(seed: int, multiple: float, target_dd: float):
    """Monthly equity from CAPITAL to CAPITAL × multiple whose max drawdown is target_dd (negative)."""
    rng = random.Random(seed)
    z = [rng.gauss(0, 1) for _ in range(MONTHS - 1)]
    total = math.log(multiple)

    def build(sigma):
        mean = sum(z) / len(z)
        steps = [total / len(z) + sigma * (x - mean) for x in z]  # demeaned noise keeps the end fixed
        values, v = [CAPITAL], float(CAPITAL)
        for s in steps:
            v *= math.exp(s)
            values.append(v)
        return values

    lo, hi = 0.0, 0.5
    for _ in range(60):  # bisection on noise size until the drawdown matches
        mid = (lo + hi) / 2
        if max_drawdown(build(mid)) > target_dd:
            lo = mid
        else:
            hi = mid
    return build((lo + hi) / 2)


def run(seed, multiple, metrics, in_sample, out_of_sample, trades, profit_factor, avg_trade, expectancy):
    dates = list(months())
    values = curve(seed, multiple, float(metrics["maxDrawdown"]))
    peak = values[0]
    drawdown = []
    for d, v in zip(dates, values):
        peak = max(peak, v)
        drawdown.append([d, f"{v / peak - 1:.4f}"])
    monthly = [[d[:7], f"{values[i] / values[i - 1] - 1:.4f}"] for i, d in enumerate(dates) if i > 0]
    full = dict(metrics, trades=trades, profitFactor=profit_factor, avgTradePct=avg_trade, expectancyR=expectancy, multiple=f"{multiple:.2f}")
    return {
        "runNumber": 1,
        "periodStart": dates[0],
        "periodEnd": "2026-06-30",
        "oosStart": OOS_START,
        "initialCapital": f"{CAPITAL}.00",
        "finishedAt": "2026-06-30T22:00:00Z",
        "assumptions": {
            "fees": "0.00 per order",
            "slippageBps": 5,
            "universe": "Current US large caps (illustrative)",
            "survivorship": "Uses today's universe: results are biased upward until point-in-time data is licensed.",
            "benchmark": {"name": "S&P 500", "multiple": "3.72"},
        },
        "metrics": {"full": full, "inSample": in_sample, "outOfSample": out_of_sample},
        "equityCurve": [[d, f"{v:.2f}"] for d, v in zip(dates, values)],
        "drawdownCurve": drawdown,
        "monthlyReturns": monthly,
    }


def param(key, label, unit, default, minimum, maximum, step):
    return {"key": key, "label": label, "unit": unit, "default": default, "min": minimum, "max": maximum, "step": step}


STRATEGIES = [
    {
        "code": "base_breakout",
        "name": "Base Breakout v3",
        "description": "Buys clean breakouts from 4–10 week bases on heavy volume.",
        "rules": [
            "A base of 4–10 weeks (default 6) no deeper than 20% that closes mostly in its upper half.",
            "Today clears the base high on volume of at least the volume multiple × the 20-day average.",
            "Relative strength rank at or above the minimum.",
            "Buy stop 0.3% above the base high; stop 1.5× ATR(14) below entry; target at the base high plus twice the base depth.",
        ],
        "status": "active",
        "liveSince": "2025-03-03",
        "schema": [
            param("baseWeeks", "Base length", "wk", 6, 4, 10, 1),
            param("volumeMultiple", "Volume multiple", "×", "1.8", "1.2", "3.0", "0.1"),
            param("minRelStrength", "Min. relative strength", "pct", 80, 50, 95, 5),
            param("stopAtr", "Stop distance", "× ATR", "1.50", "1.00", "3.00", "0.25"),
        ],
        "versions": [
            {"number": 1, "state": "archived", "params": {"baseWeeks": 8, "volumeMultiple": "1.5", "minRelStrength": 70, "stopAtr": "2.00"}},
            {"number": 2, "state": "archived", "params": {"baseWeeks": 6, "volumeMultiple": "1.5", "minRelStrength": 75, "stopAtr": "1.75"}},
            {"number": 3, "state": "published", "params": {"baseWeeks": 6, "volumeMultiple": "1.8", "minRelStrength": 80, "stopAtr": "1.50"}},
        ],
        "live": {"sinceInceptionPct": "0.114", "mtdPct": "0.028", "mtdPnl": "3240.55", "mtdTrades": 4},
        "run": dict(
            seed=11, multiple=6.02,
            metrics={"cagr": "0.169", "sharpe": "1.31", "maxDrawdown": "-0.148", "winRate": "0.46"},
            in_sample={"cagr": "0.184", "sharpe": "1.42", "maxDrawdown": "-0.148", "winRate": "0.47"},
            out_of_sample={"cagr": "0.141", "sharpe": "1.11", "maxDrawdown": "-0.123", "winRate": "0.44"},
            trades=412, profit_factor="1.86", avg_trade="0.0190", expectancy="0.42",
        ),
    },
    {
        "code": "trend_momentum",
        "name": "Trend Momentum",
        "description": "Rides leaders above a rising 21-day EMA with MACD confirmation.",
        "rules": [
            "Close above a rising 21-day EMA, with the 21-day EMA above the 50-day SMA.",
            "MACD(12,26,9) histogram positive and rising.",
            "Relative strength rank at or above the minimum.",
            "Exit on a close below the 21-day EMA or at the 2× ATR trailing stop.",
        ],
        "status": "active",
        "liveSince": "2025-01-06",
        "schema": [
            param("emaPeriod", "EMA period", "days", 21, 10, 50, 1),
            param("minRelStrength", "Min. relative strength", "pct", 70, 50, 95, 5),
            param("stopAtr", "Trailing stop", "× ATR", "2.00", "1.00", "4.00", "0.25"),
        ],
        "versions": [{"number": 1, "state": "published", "params": {"emaPeriod": 21, "minRelStrength": 70, "stopAtr": "2.00"}}],
        "live": {"sinceInceptionPct": "0.089", "mtdPct": "0.016", "mtdPnl": "1890.10", "mtdTrades": 3},
        "run": dict(
            seed=12, multiple=4.51,
            metrics={"cagr": "0.140", "sharpe": "1.12", "maxDrawdown": "-0.189", "winRate": "0.52"},
            in_sample={"cagr": "0.151", "sharpe": "1.19", "maxDrawdown": "-0.189", "winRate": "0.53"},
            out_of_sample={"cagr": "0.118", "sharpe": "0.98", "maxDrawdown": "-0.152", "winRate": "0.50"},
            trades=286, profit_factor="1.64", avg_trade="0.0210", expectancy="0.35",
        ),
    },
    {
        "code": "trend_pullback",
        "name": "Trend Pullback",
        "description": "Buys orderly dips to the 50-day once RSI resets below 45.",
        "rules": [
            "Uptrend: close above the 200-day SMA and the 50-day SMA rising.",
            "Price pulls back to within 2% of the 50-day SMA while RSI(14) drops below the reset level.",
            "Buy on the first close back above the prior day's high; stop 1.5× ATR below entry.",
        ],
        "status": "active_guarded",
        "liveSince": "2025-06-02",
        "schema": [
            param("smaPeriod", "Pullback average", "days", 50, 20, 100, 5),
            param("rsiReset", "RSI reset level", "", 45, 30, 55, 1),
            param("stopAtr", "Stop distance", "× ATR", "1.50", "1.00", "3.00", "0.25"),
        ],
        "versions": [{"number": 1, "state": "published", "params": {"smaPeriod": 50, "rsiReset": 45, "stopAtr": "1.50"}}],
        "live": {"sinceInceptionPct": "0.041", "mtdPct": "-0.006", "mtdPnl": "-712.40", "mtdTrades": 2},
        "run": dict(
            seed=13, multiple=3.64,
            metrics={"cagr": "0.119", "sharpe": "1.27", "maxDrawdown": "-0.119", "winRate": "0.61"},
            in_sample={"cagr": "0.126", "sharpe": "1.33", "maxDrawdown": "-0.119", "winRate": "0.62"},
            out_of_sample={"cagr": "0.104", "sharpe": "1.12", "maxDrawdown": "-0.097", "winRate": "0.59"},
            trades=338, profit_factor="1.71", avg_trade="0.0120", expectancy="0.31",
        ),
    },
    {
        "code": "rsi2_mean_reversion",
        "name": "RSI(2) Mean Reversion",
        "description": "Buys short, sharp dips in long-term uptrends when 2-day RSI drops below 10.",
        "rules": [
            "Long-term uptrend: close above the 200-day SMA.",
            "RSI(2) closes below the entry level.",
            "Exit when RSI(2) closes above the exit level or after 10 sessions.",
        ],
        "status": "active",
        "liveSince": "2025-09-01",
        "schema": [
            param("entryBelow", "RSI(2) entry below", "", 10, 2, 25, 1),
            param("exitAbove", "RSI(2) exit above", "", 70, 50, 90, 5),
            param("maxHoldDays", "Max holding period", "days", 10, 3, 20, 1),
        ],
        "versions": [{"number": 1, "state": "published", "params": {"entryBelow": 10, "exitAbove": 70, "maxHoldDays": 10}}],
        "live": {"sinceInceptionPct": "0.018", "mtdPct": "0.003", "mtdPnl": "355.20", "mtdTrades": 6},
        "run": dict(
            seed=14, multiple=2.27,
            metrics={"cagr": "0.074", "sharpe": "0.78", "maxDrawdown": "-0.162", "winRate": "0.64"},
            in_sample={"cagr": "0.081", "sharpe": "0.84", "maxDrawdown": "-0.162", "winRate": "0.65"},
            out_of_sample={"cagr": "0.058", "sharpe": "0.63", "maxDrawdown": "-0.131", "winRate": "0.62"},
            trades=690, profit_factor="1.32", avg_trade="0.0060", expectancy="0.12",
        ),
    },
    {
        "code": "earnings_drift",
        "name": "Earnings Drift",
        "description": "Holds stocks that gap up on earnings with heavy volume through the post-earnings drift.",
        "rules": [
            "Earnings gap up of at least the minimum gap on at least the volume multiple × the 20-day average.",
            "Entry on the first session that holds above the gap-day low.",
            "Exit after the holding period or on a close below the gap-day low.",
        ],
        "status": "active",
        "liveSince": "2025-09-15",
        "schema": [
            param("minGapPct", "Min. earnings gap", "%", "4.0", "2.0", "10.0", "0.5"),
            param("volumeMultiple", "Volume multiple", "×", "2.0", "1.2", "4.0", "0.1"),
            param("holdDays", "Holding period", "days", 20, 5, 60, 5),
        ],
        "versions": [{"number": 1, "state": "published", "params": {"minGapPct": "4.0", "volumeMultiple": "2.0", "holdDays": 20}}],
        "live": {"sinceInceptionPct": "0.023", "mtdPct": "0.004", "mtdPnl": "498.75", "mtdTrades": 1},
        "run": dict(
            seed=15, multiple=2.61,
            metrics={"cagr": "0.087", "sharpe": "0.84", "maxDrawdown": "-0.216", "winRate": "0.49"},
            in_sample={"cagr": "0.095", "sharpe": "0.90", "maxDrawdown": "-0.216", "winRate": "0.50"},
            out_of_sample={"cagr": "0.068", "sharpe": "0.70", "maxDrawdown": "-0.174", "winRate": "0.47"},
            trades=154, profit_factor="1.41", avg_trade="0.0240", expectancy="0.22",
        ),
    },
]


def main():
    catalog = []
    for s in STRATEGIES:
        entry = {k: v for k, v in s.items() if k != "run"}
        entry["run"] = run(**s["run"])
        catalog.append(entry)
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps({"generatedBy": "tools/gen_strategy_seed.py", "strategies": catalog}, indent=1) + "\n")
    for s in catalog:
        eq = [float(v) for _, v in s["run"]["equityCurve"]]
        print(f"{s['name']:24} ×{eq[-1] / eq[0]:.2f}  maxDD {max_drawdown(eq):.3f}")


if __name__ == "__main__":
    main()
