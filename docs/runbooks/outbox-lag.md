# OutboxLag (high)

**What:** integration events wait more than 60 s for dispatch, for 5 minutes (`outbox_lag_seconds`).

**Impact:**
- Late Activity items, realtime updates and push.
- A late position monitor too, since it reads `QuotesUpdated` from the outbox (see [position-monitor-lag.md](position-monitor-lag.md)).

**Confirm:**
1. `SELECT count(*), min(occurred_at) FROM infra.outbox_messages WHERE processed_at IS NULL AND failed_at IS NULL;`
2. Poison messages: `SELECT id, type, attempts, last_error FROM infra.outbox_messages WHERE failed_at IS NOT NULL ORDER BY occurred_at DESC LIMIT 20;`
3. In the traces, check that `outbox …` spans are slow or erroring for one event type.

**Fix:**
- One handler failing repeatedly blocks only its own deliveries. Fix it and redeploy; retries resume with backoff.
- Throughput: confirm the dispatcher runs (one per API instance, `SKIP LOCKED`), and scale out.
- Never delete unprocessed rows: they are the delivery guarantee.

**Escalate:** if the backlog grows for 30 minutes, or there are poison messages of `paper.*` types.
