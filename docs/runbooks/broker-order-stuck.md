# BrokerOrderStuck (high)

**What:** an Alpaca entry order has been `submitting` (not yet acknowledged by Alpaca) or `submitted` (acknowledged, not filled or closed) for more than 10 minutes (§17.5, T-144). `BrokerSyncJob` counts these every minute into `broker_orders_stuck`.

**Impact:** the user sees "Sending to Alpaca…" with no outcome, and the order's worst-case cost stays reserved, so their buying power is lower than it should be.

**Confirm:**
1. `SELECT id, user_id, symbol, status, client_order_id, broker_order_id, submitted_at FROM trading.orders WHERE broker = 'alpaca' AND purpose = 'entry' AND status IN ('submitting', 'submitted') AND submitted_at < now() - interval '10 minutes';`
2. `submitting`: check the job queue for its `paper.broker_submit.v1` job: `SELECT * FROM infra.job_queue WHERE payload::text LIKE '%<order id>%' ORDER BY created_at DESC;`. A dead job means Alpaca kept failing (look at `last_error`).
3. `submitted`: look the order up in the Alpaca dashboard by its `client_order_id`. A stop entry that hasn't triggered stays `new` at Alpaca until the day ends; that is expected for a buy-stop and resolves at the close.
4. Check the connection: `SELECT status, last_error, last_sync_at FROM trading.broker_connections WHERE user_id = …;`.

**Fix:**
- Alpaca or the network was down: once it recovers, `BrokerSyncJob` settles the order on its next pass. Nothing to do.
- The keys were revoked: the connection shows `degraded`; the user reconnects in Settings. The next sync rejects the order and releases the reservation.
- Never resubmit by hand: the submit job always looks up by `client_order_id` first, and a manual order would duplicate it.

**Escalate:** if orders stay stuck for 30 minutes with Alpaca healthy (status.alpaca.markets), as a bug in the order flow.
