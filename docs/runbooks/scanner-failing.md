# ScannerFailing (medium)

**What:** no successful scanner run for more than two 15-minute intervals during the session (`scanner_last_success_timestamp`).

**Impact:**
- Discover shows "Scanner unavailable" (`SCN-504` or `SCN-500`), and signals are hidden rather than shown stale.
- No new proposals from signals; existing positions are unaffected.

**Confirm:**
1. `SELECT started_at, status, error_code, symbols_evaluated FROM discovery.scanner_runs ORDER BY started_at DESC LIMIT 5;`
2. `SCN-503` (no usable data): check market data (see [market-data-stale.md](market-data-stale.md)).
3. `SCN-500`: find the exception in the logs ("Scanner run … failed"). In Development, check whether the `/dev/faults` scanner fault was left on.

**Fix:** fix the data or the bug, then trigger a manual run (enqueue `discovery.scanner_run.v1` with trigger `manual`) or wait for the next interval.

**Escalate:** if it lasts a whole session.
