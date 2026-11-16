# Database: pool saturation, backups, disk (high)

**What:**
- `DatabasePoolSaturation`: Npgsql pool use is above 80 % (`db_client_connection_count / db_client_connection_max`) for 5 minutes.
- Backup failures and disk above 80 % come from the managed Postgres provider's own monitoring (T-117).

**Impact:** requests wait for connections, which shows up as latency, then timeouts and 5xx on every endpoint.

**Confirm:**
1. `SELECT state, count(*) FROM pg_stat_activity GROUP BY state;`
2. Look for long transactions: `SELECT pid, now() - xact_start AS age, query FROM pg_stat_activity WHERE xact_start IS NOT NULL ORDER BY age DESC LIMIT 10;`
3. Slow queries: the Npgsql spans in traces (`db.query.text`).

**Fix:**
- Kill a runaway session only after identifying it: `SELECT pg_terminate_backend(pid);`
- Scale API instances down if the total connections exceed Postgres `max_connections`, or raise the pool size per instance.
- Backups or disk: follow the provider's runbook. Restore drills are part of T-117.

**Escalate:** if saturation persists after clearing stuck sessions, or a backup fails twice.
