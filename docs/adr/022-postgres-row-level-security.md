# ADR 022: Postgres row-level security, evaluated and deferred

- **Status:** DECIDED (2026-11-16, T-115). Revisit before real-money trading (§17.9) or a second application touches the database.
- **Context:** SYSTEM_DESIGN.md §18.5. User data is isolated in the application:
  - every user-owned entity implements `IUserOwned`, and each module's `DbContext` adds a global query filter on `user_id` from the request's `ICurrentUser`;
  - a foreign id answers 404;
  - `UserOwnershipTests` (architecture) checks every user-owned entity has the filter;
  - `DataIsolationTests` checks every parameterised route against another user's resource.

  The question is whether Postgres row-level security (RLS) should enforce the same rule in the database.

## Options

1. **Application filters only (current).**
   - For: one mechanism; tested end to end; system jobs (reconciliation, push, monitor, guard) read across users explicitly with `IgnoreQueryFilters()`, which is easy to spot in review.
   - Against: a missed filter or a raw SQL query is not stopped by the database.
2. **RLS on user-owned tables, with `SET LOCAL app.user_id` per transaction.**
   - For: defence in depth against application bugs and ad-hoc SQL.
   - Against:
     - every request and job must set the GUC inside a transaction (pooled connections make a forgotten reset leak across users);
     - system jobs need a `BYPASSRLS` role or per-job policies, so a second connection string and role split;
     - EF's interpolated raw SQL and `ExecuteUpdate` paths must all run in that context;
     - integration tests need the same roles;
     - a policy mistake fails closed (no rows) and is easy to misread as "no data".
3. **RLS only as an audit tripwire** (policies in a "log, don't block" mode). Postgres has no such mode; it would need triggers. Rejected.

## Decision

Keep application-level isolation (option 1) for the MVP, and strengthen it:

- `UserOwnershipTests` and `DataIsolationTests` stay mandatory in CI (T-001); new routes cannot merge without an isolation fixture;
- cross-user reads stay limited to named system services (`IgnoreQueryFilters` appears only in jobs, relays, the monitor and the explicitly owned lookups);
- the API's database role keeps no `DELETE` or `UPDATE` grant on the append-only ledger (§10.4).

## Consequences

- **Revisit:** RLS (option 2) is the planned step before eToro Real (§17.9 gates) or any shared database access (analytics, admin tools). That work will add an `astryx_app` role without `BYPASSRLS`, an `astryx_jobs` role with it, and a `UserScopedConnectionInterceptor` that sets `app.user_id` per transaction.
- **Risk accepted for paper trading:** a filter bug could expose another user's paper data. It is mitigated by the two test suites and by the absence of any real money or credentials until T-141, where eToro keys are additionally envelope-encrypted per user.
