# OrderErrorRate (high)

**What:** more than 1 % of order requests (`/paper/orders`, `/positions/*/close`) returned 5xx over 10 minutes. 4xx answers (risk checks, stale quotes, closed market) are expected and excluded.

**Impact:** users cannot place or close paper positions.

**Confirm:**
1. In the traces, filter `http.route` to the order routes with status 5xx and open one. The span shows which step threw (gate, ledger, position book).
2. In the logs, search the failing request's `CorrelationId`.
3. Usual suspects: database errors (`53300` too many connections, deadlocks `40P01`), the outbox write in the same transaction, a deploy mid-request.

**Fix:**
- Database: see [database.md](database.md).
- A bug after a deploy: roll back. Idempotency keys make client retries safe, so a replay returns the first result and never a second fill.
- Verify afterwards that nothing was half-applied: run the reconciliation for affected portfolios (`PortfolioReconciler`).

**Escalate:** if the rate stays above 1 % for 30 minutes, or any user reports a duplicate or a missing fill.
