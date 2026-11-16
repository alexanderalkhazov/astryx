# ReconciliationMismatch (critical)

**What:** the nightly `ReconciliationJob` (§20.13) found at least one issue. Each affected portfolio now has `reconciliation_halted_at` set: new entries are refused with 409 `portfolio_halted`, and exits still work.

**Impact:** the affected users cannot open positions until someone reviews and clears the halt.

**Confirm:**
1. `SELECT id, user_id, reconciliation_halted_at, reconciliation_issues FROM portfolio.portfolios WHERE reconciliation_halted_at IS NOT NULL;`
2. Find the audit rows: `SELECT * FROM audit.audit_logs WHERE action = 'portfolio.reconciliation_failed' ORDER BY occurred_at DESC;`
3. Each issue names its check: `ledger:` (cash cache against the ledger), `position …` (quantity or realized P&L against fills), `portfolio realized_pnl`, `order …` (working past expiry), `… missing a working stop/target exit`, or `snapshot`.

**Fix:**
- **Treat it as a bug until proven otherwise.** Do not edit the ledger: it is append-only by design.
- Ledger drift: recompute with `LedgerInvariants.CheckAsync` locally against a restored copy. If a correction is needed, post an `adjustment` entry through code with an audit note.
- Overdue working order: run `DayOrderExpiryJob` once, then reconcile again.
- Missing OCO leg: work out why the leg was cancelled from the order history (`trading.orders`), then re-create the legs through a reviewed script.
- **Clearing the halt is manual:** `UPDATE portfolio.portfolios SET reconciliation_halted_at = NULL, reconciliation_issues = NULL WHERE id = …;` only after the root cause is fixed, with an audit entry.

**Escalate:** always. Tell the product owner; the halted users see "New entries paused".
