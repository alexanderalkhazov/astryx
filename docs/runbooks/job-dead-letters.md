# JobDeadLetters (medium)

**What:** at least one job was dead-lettered (`jobs_dead_total{type}`). It ran out of attempts, or had an unknown type or an unreadable payload.

**Impact:** depends on the type:
- `notifications.push.v1`: one push not sent;
- `portfolio.reconciliation.v1`: a nightly check skipped;
- `market.*`: data gaps until the next run.

**Confirm:**
1. `SELECT id, type, attempts, last_error, created_at FROM infra.job_queue WHERE status = 'dead' ORDER BY created_at DESC LIMIT 20;`
2. Search the logs for the job id: the failing attempts log at Warning, and the final one at Error.

**Fix:**
- Fix the cause, then requeue: `UPDATE infra.job_queue SET status = 'pending', attempts = 0, run_after = now(), last_error = NULL WHERE id = …;`
- Recurring jobs reschedule themselves, so a single dead run of a recurring job usually needs no requeue.

**Escalate:** if dead jobs repeat for the same type, or any `paper.*` or `portfolio.*` job is dead.
