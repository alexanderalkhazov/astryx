# PositionMonitorLag (critical)

**What:** `position_monitor_lag_seconds` > 30 for 2 minutes during the session. The position monitor evaluates stops, targets and working buy stops when `QuotesUpdated` arrives through the outbox. Lag means quotes are being acted on late.

**Impact:**
- Stops and targets can fill late, at worse prices than planned.
- Working entries can trigger late.
- Paper money only in the MVP, but it is the core safety promise.

**Confirm:**
1. In the OTel backend, check `outbox_lag_seconds`. If it is also high, the outbox is the bottleneck (see [outbox-lag.md](outbox-lag.md)).
2. Check `market_quote_age_seconds`. If quotes themselves are old, see [market-data-stale.md](market-data-stale.md).
3. In the logs, search for `PositionMonitor` errors and for slow `outbox market.quotes_updated.v1` spans.

**Fix:**
- Outbox backlog: check the API is running and not CPU-starved, then scale the API or worker. Never delete outbox rows.
- Database contention (long lock waits on `portfolio.positions`): find the blocker with `SELECT * FROM pg_stat_activity WHERE wait_event_type = 'Lock'`.
- If the lag will not clear before the close, consider pausing new entries. The daily halt cannot be forced, but proposals can be left unapproved. Exits still fire once the monitor catches up.

**Escalate:** right away if lag exceeds 5 minutes, or if any stop filled more than 1 % past its level.
