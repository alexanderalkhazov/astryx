#!/usr/bin/env python3
"""Golden files for the quant indicator tests (SYSTEM_DESIGN.md §13.6, TASKS.md T-060).

An independent reference implementation of the §13.3 formulas, written from the spec rather than from the C# code.
It uses exact rational arithmetic (fractions.Fraction), so any difference from the C# decimal implementation comes
from the C# side. Volatility uses floats exactly where the spec allows (the log step).

Standard library only. Run manually and commit the output; review diffs like code:

    python3 tools/gen_golden.py            # every tests/fixtures/quant/*_daily.csv

Each input <name>_daily.csv (session_date,open,high,low,close,volume) produces <name>_daily.expected.csv with one
row per bar and one column per indicator (empty during warm-up), plus <name>_daily.summary.csv with the
single-value results (relative-strength score, trend).
"""

from __future__ import annotations

import csv
import math
import sys
from decimal import ROUND_HALF_EVEN, Decimal
from fractions import Fraction
from pathlib import Path

FIXTURES = Path(__file__).resolve().parent.parent / "tests" / "fixtures" / "quant"
PLACES = 12  # written precision; tests compare at 1e-6


def sma(values, n):
    out = [None] * len(values)
    for t in range(n - 1, len(values)):
        out[t] = sum(values[t - n + 1 : t + 1], Fraction(0)) / n
    return out


def ema(values, n):
    """k = 2/(n+1), seeded with the SMA of the first n values."""
    out = [None] * len(values)
    if len(values) < n:
        return out
    k = Fraction(2, n + 1)
    prev = sum(values[:n], Fraction(0)) / n
    out[n - 1] = prev
    for t in range(n, len(values)):
        prev = values[t] * k + prev * (1 - k)
        out[t] = prev
    return out


def rsi(closes, n=14):
    out = [None] * len(closes)
    if len(closes) < n + 1:
        return out
    gains = [max(closes[i] - closes[i - 1], 0) for i in range(1, len(closes))]
    losses = [max(closes[i - 1] - closes[i], 0) for i in range(1, len(closes))]
    ag = Fraction(sum(gains[:n], Fraction(0))) / n
    al = Fraction(sum(losses[:n], Fraction(0))) / n

    def value(ag, al):
        if al == 0:
            return Fraction(50) if ag == 0 else Fraction(100)
        return 100 - Fraction(100) / (1 + ag / al)

    out[n] = value(ag, al)
    for t in range(n + 1, len(closes)):
        ag = (ag * (n - 1) + gains[t - 1]) / n
        al = (al * (n - 1) + losses[t - 1]) / n
        out[t] = value(ag, al)
    return out


def macd(closes, fast=12, slow=26, signal=9):
    count = len(closes)
    line = [None] * count
    sig = [None] * count
    hist = [None] * count
    if count < slow + signal - 1:
        return line, sig, hist
    f, s = ema(closes, fast), ema(closes, slow)
    for t in range(slow - 1, count):
        line[t] = f[t] - s[t]
    on_line = ema(line[slow - 1 :], signal)
    for k, v in enumerate(on_line):
        if v is not None:
            t = slow - 1 + k
            sig[t] = v
            hist[t] = line[t] - v
    return line, sig, hist


def atr(bars, n=14):
    out = [None] * len(bars)
    if len(bars) < n + 1:
        return out

    def tr(t):
        _, high, low, _, _ = bars[t]
        prev_close = bars[t - 1][3]
        return max(high - low, abs(high - prev_close), abs(low - prev_close))

    value = sum((tr(t) for t in range(1, n + 1)), Fraction(0)) / n
    out[n] = value
    for t in range(n + 1, len(bars)):
        value = (value * (n - 1) + tr(t)) / n
        out[t] = value
    return out


def relative_volume(volumes, n=20):
    out = [None] * len(volumes)
    for t in range(n, len(volumes)):
        average = Fraction(sum(volumes[t - n : t]), n)
        out[t] = None if average == 0 else volumes[t] / average
    return out


def roc(closes, n=20):
    out = [None] * len(closes)
    for t in range(n, len(closes)):
        out[t] = None if closes[t - n] == 0 else closes[t] / closes[t - n] - 1
    return out


def volatility(closes, n=20):
    """Sample stdev of log returns × √252 in floats (the one allowed float step), then 15 significant digits → 6 dp."""
    out = [None] * len(closes)
    floats = [float(c) for c in closes]
    for t in range(n, len(closes)):
        returns = [math.log(floats[i] / floats[i - 1]) for i in range(t - n + 1, t + 1)]
        mean = 0.0
        for r in returns:
            mean += r
        mean /= n
        squares = 0.0
        for r in returns:
            squares += (r - mean) * (r - mean)
        annualised = math.sqrt(squares / (n - 1)) * math.sqrt(252)
        # C#'s (decimal)double keeps 15 significant digits before rounding.
        out[t] = Fraction(Decimal("%.15g" % annualised).quantize(Decimal("0.000001"), rounding=ROUND_HALF_EVEN))
    return out


def rs_score(closes):
    if len(closes) < 253:
        return None
    last = closes[-1]
    legs = [(63, Fraction(2, 5)), (126, Fraction(1, 5)), (189, Fraction(1, 5)), (252, Fraction(1, 5))]
    return sum((w * (last / closes[-(p + 1)] - 1) for p, w in legs), Fraction(0))


def trend(closes):
    def rule(fast, slow):
        f, s = sma(closes, fast), sma(closes, slow)
        c, fn, sn = closes[-1], f[-1], s[-1]
        slope = fn - f[-11]
        if c > fn > sn and slope > 0:
            return "uptrend"
        if c < fn < sn and slope < 0:
            return "downtrend"
        return "sideways"

    if len(closes) >= 200:
        return rule(50, 200), False
    if len(closes) >= 50:
        return rule(20, 50), True
    return "insufficient", True


def fmt(value):
    if value is None:
        return ""
    return str((Decimal(value.numerator) / Decimal(value.denominator)).quantize(Decimal(1).scaleb(-PLACES), rounding=ROUND_HALF_EVEN))


def generate(source: Path) -> None:
    with source.open(newline="") as handle:
        rows = list(csv.DictReader(handle))
    dates = [r["session_date"] for r in rows]
    bars = [(Fraction(r["open"]), Fraction(r["high"]), Fraction(r["low"]), Fraction(r["close"]), int(r["volume"])) for r in rows]
    closes = [b[3] for b in bars]
    volumes = [b[4] for b in bars]
    line, sig, hist = macd(closes)
    columns = {
        "sma20": sma(closes, 20),
        "sma50": sma(closes, 50),
        "ema20": ema(closes, 20),
        "rsi14": rsi(closes, 14),
        "macd": line,
        "macd_signal": sig,
        "macd_hist": hist,
        "atr14": atr(bars, 14),
        "relvol20": relative_volume(volumes, 20),
        "roc20": roc(closes, 20),
        "vol20": volatility(closes, 20),
    }

    target = source.with_name(source.stem + ".expected.csv")
    with target.open("w", newline="") as handle:
        writer = csv.writer(handle, lineterminator="\n")
        writer.writerow(["session_date", *columns])
        for i, date in enumerate(dates):
            writer.writerow([date, *(fmt(values[i]) for values in columns.values())])

    state, short = trend(closes)
    summary = source.with_name(source.stem + ".summary.csv")
    with summary.open("w", newline="") as handle:
        writer = csv.writer(handle, lineterminator="\n")
        writer.writerow(["metric", "value"])
        writer.writerow(["rs_score", fmt(rs_score(closes))])
        writer.writerow(["trend", state])
        writer.writerow(["trend_short_history", str(short).lower()])
    print(f"{source.name}: {len(rows)} bars → {target.name}, {summary.name}")


def main() -> int:
    sources = sorted(FIXTURES.glob("*_daily.csv"))
    if not sources:
        print(f"No *_daily.csv in {FIXTURES}", file=sys.stderr)
        return 1
    for source in sources:
        generate(source)
    return 0


if __name__ == "__main__":
    sys.exit(main())
