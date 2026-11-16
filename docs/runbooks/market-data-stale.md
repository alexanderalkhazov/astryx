# MarketDataStale (high)

**What:** the oldest stored quote is more than 180 s old for 3 minutes during the session (`market_quote_age_seconds`).

**Impact:**
- Approvals are disabled (the risk engine's `data_fresh` check).
- The app shows "Market data connection lost / delayed".
- Stops and targets act on old prices until quotes resume.

**Confirm:**
1. `GET /api/v1/system/status`: check `marketData.feedDown`, `quotesAsOf` and `quotesStale`.
2. `SELECT dataset, last_success_at, last_error_code, consecutive_failures FROM market.provider_sync_state;`
3. Check the `market_provider_requests_total{outcome!="ok"}` rate: `rate_budget` means the plan's limit was hit, `unavailable` means the provider is down.

**Fix:**
- Provider outage: nothing to do in the app, which stays safe (approvals off). Check the provider's status page.
- Rate budget: lower the universe or raise `MarketData:Ingestion:RequestsPerMinute` within the plan.
- Worker not running: check that the API's job worker is alive (job spans `job market.quote_sync.v1` every minute).

**Escalate:** if it lasts more than 15 minutes during the session.
