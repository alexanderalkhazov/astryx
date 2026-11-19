# Strategy rules — DRAFT for owner approval (Q-09)

Status: **PROPOSED (2026-11-19).** Nothing here is implemented. Once approved, the rules move into SYSTEM_DESIGN.md §13.4.x next to Base Breakout (§13.4.1) and become `<code>@1` rule implementations.

## Ground rules shared by all four

- **Long only, daily bars, regular session.** Signals are evaluated by the same 15-minute scanner as Base Breakout, using the forming daily bar (volume projected to a full session, as Base Breakout does since 944153a).
- **Inputs available today:** daily OHLCV (2 years), SMA, EMA, Wilder RSI, MACD (12/26/9), Wilder ATR(14), relative volume (20), the RS percentile rank (1–99) across the S&P 500, and the trend state (`uptrend` / `sideways` / `downtrend`).
- **Orders:** every signal becomes the same bracket the app already sends: an entry (buy stop, limit or market), a stop-loss leg and a take-profit leg. The risk engine sizes and gates it exactly as today.
- **Parameters** are the ones already published in the catalog (`catalog.json`), so the Strategy lab screens don't change.
- **Score** reuses the §13.4 weights: `30·setup + 20·rank/99 + 20·volume + 15·trend + 15·min(RR/3, 1)`, with "setup" defined per strategy below.
- **Same stop formula everywhere:** `round_cent(entry − stopAtr × ATR14)`, unless stated.

## 1. Trend Momentum (`trend_momentum@1`)

*Catalog:* "Rides leaders above a rising 21-day EMA with MACD confirmation." Params: `emaPeriod` 21, `minRelStrength` 70, `stopAtr` 2.00.

| Step | Proposed definition |
|---|---|
| Conditions (all) | Close > EMA21 · EMA21 today > EMA21 5 sessions ago (rising) · MACD line > signal line, and the histogram rose today · RS rank ≥ 70 · trend = `uptrend` · close within 8% of the 52-week high |
| Entry | Buy stop at `ceil_to_0.10(today's high × 1.002)` (continuation, not a chase) |
| Stop | `entry − 2.00 × ATR14` |
| Target | `entry + 3 × (entry − stop)` → R:R 3.0 |
| Setup quality | `0.5 × (1 − distance from 52w high / 8%) + 0.5 × min(EMA21 slope over 5 sessions / 3%, 1)` |
| Fail codes | `below_ema`, `ema_not_rising`, `no_macd_confirmation`, `weak_relative_strength`, `not_uptrend`, `far_from_high` |
| Reason | "Above a rising 21-day EMA with MACD turning up; RS {rank}." |

## 2. Trend Pullback (`trend_pullback@1`)

*Catalog:* "Buys orderly dips to the 50-day once RSI resets below 45." Params: `smaPeriod` 50, `rsiReset` 45, `stopAtr` 1.50. (Already shown as "Active · guard" — the drawdown guard applies.)

| Step | Proposed definition |
|---|---|
| Conditions (all) | SMA50 rising (today > 10 sessions ago) and close > SMA200 · RSI(14) touched ≤ 45 within the last 5 sessions · today's low within 1 × ATR14 of SMA50 and close above SMA50 · today closes above yesterday's high (the turn) · pullback depth from the 20-day high ≤ 12% |
| Entry | Buy stop at `ceil_to_0.10(today's high × 1.002)` |
| Stop | `round_cent(min(entry − 1.50 × ATR14, SMA50 − 0.25 × ATR14))`: the lower of the usual ATR stop and just under the 50-day, so a normal test of the average doesn't stop it out |
| Target | The 20-day high before the pullback; if that gives R:R < 1.5, the signal is dropped (`reward_too_small`) |
| Setup quality | `0.5 × (1 − depth / 12%) + 0.5 × (RSI recovery: (RSI today − min RSI) / 20, capped at 1)` |
| Fail codes | `sma_not_rising`, `below_sma200`, `rsi_not_reset`, `not_at_sma`, `no_turn`, `pullback_too_deep`, `reward_too_small` |
| Reason | "Pulled back {depth}% to the 50-day; RSI reset to {minRsi} and turned up." |

## 3. RSI(2) Mean Reversion (`rsi2_mean_reversion@1`)

*Catalog:* "Buys short, sharp dips in long-term uptrends when 2-day RSI drops below 10." Params: `entryBelow` 10, `exitAbove` 70, `maxHoldDays` 10.

| Step | Proposed definition |
|---|---|
| Conditions (all) | Close > SMA200 · RSI(2) < 10 at today's close · not down more than 15% over 5 sessions (avoids news collapses) · average dollar volume (20) ≥ $50M |
| Entry | Limit at `round_cent(close × 0.995)` for the next session (buy the dip, don't chase) |
| Stop | `entry − 2.5 × ATR14` (wide by design; mean reversion needs room) |
| Target (bracket leg) | `round_cent(SMA5)` at signal time — the classic "close back above the 5-day" exit approximated as a price |
| Setup quality | `(10 − RSI2) / 10` |
| Fail codes | `below_sma200`, `rsi2_not_oversold`, `crash_filter`, `illiquid` |
| Reason | "2-day RSI {rsi2} in a long-term uptrend; buying the dip." |

**Needs new capability:** `exitAbove` (exit when RSI(2) > 70) and `maxHoldDays` (exit after 10 sessions) are *time/indicator exits*, which a bracket can't express. Proposal: a daily `StrategyExitJob` after the close that market-sells positions whose strategy exit condition holds (sent as an Alpaca market order at the next open). Without it, positions exit only at the stop or the SMA5 target. **Decision needed:** build the exit job now, or ship with bracket exits only and add it later.

## 4. Earnings Drift (`earnings_drift@1`)

*Catalog:* "Holds stocks that gap up on earnings with heavy volume through the post-earnings drift." Params: `minGapPct` 4.0, `volumeMultiple` 2.0, `holdDays` 20.

| Step | Proposed definition |
|---|---|
| Event | A gap day within the last 3 sessions: open ≥ prior close × (1 + 4.0%) and relative volume ≥ 2.0 on that day |
| Conditions (all) | Since the gap: no close below the gap day's low · today's close ≥ gap-day close × 0.98 · RS rank ≥ 60 |
| Entry | Buy stop at `ceil_to_0.10(max(high since the gap) × 1.002)` |
| Stop | `round_cent(gap day's low − 0.25 × ATR14)` (the gap failing is the invalidation) |
| Target | `entry + 2.5 × (entry − stop)` |
| Setup quality | `0.5 × min(gap% / 10%, 1) + 0.5 × min(gap-day RV / 4, 1)` |
| Fail codes | `no_gap`, `gap_filled`, `faded`, `weak_relative_strength` |
| Reason | "Gapped up {gap}% on {rv}× volume {n} sessions ago and is holding the gap." |

**Data limitation:** Alpaca has no earnings calendar, so the rule can only see *a large gap on heavy volume*, not that it was earnings. Options: (a) accept any qualifying gap and rename the strategy "Gap & Go Drift", (b) tag gaps as earnings when Alpaca News has an earnings headline for the symbol that day (best effort), or (c) add an earnings-calendar provider (licence/cost). **Recommendation: (b).** `holdDays` 20 has the same time-exit dependency as RSI(2) above.

## Decisions requested

1. Approve, change or reject each rule's conditions, entry, stop and target.
2. RSI(2) and Earnings Drift time exits: build the daily `StrategyExitJob` now, or bracket-only for v1?
3. Earnings Drift without an earnings calendar: option (a), (b) or (c)?
4. Backtests: once the rules are approved, run each against the two years of Alpaca history to replace the illustrative Strategy lab figures (Q-10)?
