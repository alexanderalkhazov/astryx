# ADR 024: Alpaca Market Data as the real data provider

- **Status:** DECIDED for the prototype (owner decision, 2026-11-17: "add real market data" with the Alpaca keys). **Q-04 (licence to show the data to other users) is OPEN** and blocks distributing the app to anyone but the owner (T-118, T-119).
- **Context:** SYSTEM_DESIGN.md §16.2 (provider matrix), Q-03 (which provider), Q-04 (licence for storing and displaying the data). The fake provider served every screen until T-147. The owner supplied Alpaca paper keys and asked for real market data and real paper trading. `AlpacaMarketDataProvider` and `AlpacaNewsProvider` were then built and are running locally (240fdbb, 944153a).

## Options evaluated (2026-11-19)

| | **Alpaca Market Data** (chosen) | Massive (formerly Polygon.io) | Tiingo |
|---|---|---|---|
| Coverage used | S&P 500; real-time IEX quotes, 15-minute-delayed consolidated (SIP) day bars, consolidated history, corporate actions, calendar and news | All US exchanges on paid plans | EOD for US stocks; real-time IEX in production, multi-venue feed in beta |
| Cost for us | $0 (Basic plan, included with the brokerage account we already use); Algo Trader Plus ($99/month) for full real-time SIP | Individual plans about $0–$199/month per third-party summaries; business plans separate | Free; Power about $30/month; Commercial about $50/month |
| Rate limits | 200 requests/min on Basic (we budget 80 for ingestion) | Varies by plan | Varies by plan |
| Fit with the app | Same vendor and keys as the brokerage (ADR 023), one integration; news and corporate actions included | Second vendor and keys; no brokerage | Second vendor and keys; no brokerage, no news in the IEX product |
| Licence on the retail plan | Personal use. Alpaca's public terms are read by third parties as barring commercial redistribution without written consent; exchange agreements add display fees for re-displayed data. No primary text found that allows display to other users | "Individual Use" and non-pro plans are for a natural person's personal, non-business use only; redistribution needs a business contract | Basic and Power: internal and personal use only, "may not redistribute the data in any form"; redistribution is a separate sales contract |

## Decision

Use **Alpaca Market Data on the Basic plan** for the prototype, which only the owner uses on their own devices.
- Real-time last, bid and ask come from IEX.
- The day's OHLCV and the previous close come from the delayed consolidated feed.
- History comes from consolidated bars up to 16 minutes ago.
- The universe is the S&P 500 with GICS sectors, from an embedded list.
- The fake provider stays the default for tests, CI, E2E and the load stack.

## Consequences

- **Q-03: answered.** Alpaca, by owner decision. A paid upgrade to Algo Trader Plus is the first lever if IEX-only last prices or 15-minute-delayed volume prove too coarse for the scanner.
- **Q-04: open, and every evaluated retail plan says no.** Showing market data to anyone other than the account holder, which includes testers (T-118) and any release (T-119), needs one of these, decided by the owner:
  - **(a)** each user connects their own Alpaca account and sees data under their own licence, which needs per-user data keys;
  - **(b)** a written redistribution or display agreement with Alpaca;
  - **(c)** a vendor's business or redistribution plan.
- **Display rules until then:** data is stored and displayed only for the owner's local use. No screenshots of live data go into public materials.

## Sources

- Alpaca, About Market Data API (Basic plan: IEX real-time for equities, the default for paper and live): <https://docs.alpaca.markets/docs/about-market-data-api>
- Alpaca, data provider support article: <https://alpaca.markets/support/data-provider-alpaca>
- Third-party reading of Alpaca's terms (redistribution prohibited by default; display fees): <https://raxx-internal-docs.pages.dev/business/research/2026-06-11-paper-trading-regulatory-and-data>, <https://raxx-internal-docs.pages.dev/legal/research/alpaca-data-rights-sentiment-pipeline-2026-06-05> (secondary sources: confirm against Alpaca's current terms)
- Massive, pro vs non-pro classification (individual plans are for personal, non-business use): <https://massive.com/knowledge-base/article/what-are-pro-and-non-pro-classifications-for-massives-stock-data>, <https://www.massive.com/blog/understanding-professional-status>
- Massive pricing summaries (third-party, July 2026): <https://qveris.ai/guides/polygon-pricing-optimized/>
- Tiingo, API documentation (licensing: internal and personal use only, no redistribution): <https://api.tiingo.com/documentation>; IEX product and plans: <https://api.tiingo.com/products/iex-api>
