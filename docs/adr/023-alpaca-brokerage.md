# ADR 023: Alpaca replaces eToro as the brokerage

- **Status:** DECIDED (2026-11-17, owner decision). Supersedes ADR 020 (eToro official public API).
- **Context:** SYSTEM_DESIGN.md §17. The MVP needs a broker venue with virtual funds for approved trades, and a real-money path that stays disabled until the §17.9 gates pass. ADR 020 chose eToro (Demo, Real gated). Nothing eToro-specific was built: the code had only the `etoro_demo`/`etoro_real` mode values, the `etoro` broker code in two check constraints, and a disabled "Connect eToro" button. T-140–T-149 were all still open.

## Decision

Use **Alpaca's Trading API**: **Alpaca Paper** (`alpaca_paper`) is the MVP venue, and **Alpaca Live** (`alpaca_live`) stays designed but disabled behind §17.9. One `AlpacaBrokerAdapter` serves both environments; the environment comes from the stored connection, never from a request.

## Why Alpaca fits

- Separate paper and live base URLs **and** keys, so a paper connection can never place a live order.
- Native buy `stop` orders trigger when the price rises to the stop: the breakout entry semantics the scanner produces. eToro needed a market-if-touched order whose direction was unverified (old Q-22).
- Native `bracket` orders (entry + stop loss + take profit, managed as OCO by Alpaca) map directly to Astryx's attached stop and target.
- `client_order_id` (≤ 128 characters) gives idempotency and lookup after a lost response, mapping 1:1 to the mobile Idempotency-Key.
- A `trade_updates` WebSocket for fills, plus lookups as the fallback.
- 200 requests/minute per account, comfortably above the sync and fill budget.
- An optional Market Data API (IEX free, all exchanges on the paid plan) is a candidate for Q-03.
- The Trading API has no money-movement endpoints for this use; funding stays in Alpaca's dashboard and Broker API, which the app never calls.

## Consequences

- Mode values become `simulated`, `alpaca_paper`, `alpaca_live` (the last rejected by a check constraint); the broker code becomes `alpaca`. Migrations `AlpacaBrokerage` (Paper and Portfolio modules) change the constraints; no rows used the eToro values.
- Credentials are an API key ID and secret, envelope-encrypted as before (§17.6). Alpaca keys always carry trading permission, so the read-only key case only applies to OAuth tokens without the `trading` scope (Q-21).
- Alpaca paper fills happen at the NBBO with no slippage, market impact or regulatory fees (Q-23); receipts label them as simulated by Alpaca.
- Open: whether a bracket accepts a `stop` entry, and the response to a duplicate `client_order_id` (Q-22, verified in Paper during T-143).
- T-140–T-149 are retargeted to Alpaca; T-130–T-136 (real money) keep their gates, with Alpaca Live in place of eToro Real.
