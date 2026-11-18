# Runbooks

One page per alert in `infra/observability/alerts.yml` (SYSTEM_DESIGN.md §23.8). Each page covers what fired, the impact, how to confirm it, how to fix it, and when to escalate. Logs carry a `CorrelationId`; traces and metrics are in the OTel backend (locally the Aspire dashboard, http://localhost:18888).

| Alert | Severity | Page |
|---|---|---|
| PositionMonitorLag | Critical | [position-monitor-lag.md](position-monitor-lag.md) |
| ReconciliationMismatch | Critical | [reconciliation-mismatch.md](reconciliation-mismatch.md) |
| LiveTradingEnabled | Critical | [live-trading-enabled.md](live-trading-enabled.md) |
| BrokerOrderStuck | High | [broker-order-stuck.md](broker-order-stuck.md) |
| OrderErrorRate | High | [order-error-rate.md](order-error-rate.md) |
| MarketDataStale | High | [market-data-stale.md](market-data-stale.md) |
| OutboxLag | High | [outbox-lag.md](outbox-lag.md) |
| DatabasePoolSaturation (plus backups and disk) | High | [database.md](database.md) |
| RefreshTokenReuse, LoginFailureSpike | High | [auth-anomalies.md](auth-anomalies.md) |
| ScannerFailing | Medium | [scanner-failing.md](scanner-failing.md) |
| AiUnavailable | Medium | [ai-unavailable.md](ai-unavailable.md) |
| ApiLatency | Medium | [api-latency.md](api-latency.md) |
| JobDeadLetters | Medium | [job-dead-letters.md](job-dead-letters.md) |
