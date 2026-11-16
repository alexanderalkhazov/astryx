# ApiLatency (medium)

**What:** the API's p95 request latency is above 1 s for 10 minutes. SignalR routes are excluded.

**Impact:** a sluggish app. Approvals still work, but assessments can expire faster than users act.

**Confirm:**
1. In the traces, sort by duration. Is it one route or all of them?
2. All routes: check the database (see [database.md](database.md)), CPU and memory of the API container, and GC pauses.
3. One route: open a slow trace and find the slowest child span (usually an N+1 query or an outbound HTTP call).

**Fix:** scale out for load; fix the hot query for a single route. The load smoke (`tests/load/smoke.js`) reproduces trading-hour traffic.

**Escalate:** if p95 exceeds 3 s, or order endpoints exceed NFR-02 (500 ms) for an hour.
