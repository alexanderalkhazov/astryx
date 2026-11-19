# Astryx: Delivery Plan and Working Rules

> **Owner:** @Alex · **Created:** 2026-11-10 · **Source of truth for *what* to build:** [`SYSTEM_DESIGN.md`](SYSTEM_DESIGN.md)
> **This file is the source of truth for *order of work* and *status*.** Every contributor, human or AI agent, follows the rules in Part A before touching code.

**Goal:** a working app end to end. A user can sign up, find a setup in Discover, read the deterministic and AI analysis, approve a sized trade by holding for 1.2 s, watch the position in Portfolio and Activity, and see it close at its stop or target. The app runs on the iOS simulator against the local Docker stack and local Ollama. Trades execute either in the internal simulator (development and tests) or **in the user's Alpaca paper account through Alpaca's official Trading API** (SYSTEM_DESIGN.md §17, ADR-023). Real-money (Alpaca Live) trading stays disabled until the M8 gates pass.

---

## Part A — Working rules

### A1. Task tracking protocol (mandatory)

1. **One task, one commit** (or a small series of commits, see rule 5). Pick the **lowest-numbered unblocked** open task unless the owner says otherwise.
2. Every commit message **starts with the task ID** and uses Conventional Commits:
   `T-204 feat(market): add quote freshness evaluation`
   Changes that only maintain the plan itself (new or re-scoped tasks, design-doc updates not delivered by a task) use the prefix `PLAN`: `PLAN docs: add Alpaca integration milestone`. They are not ticked.
3. When the task's commit is in, **tick the box and record the short hash** (first 7 characters of `git rev-parse --short HEAD`) of the commit that delivered it:
   ```diff
   - - [ ] **T-204** Quote freshness rules
   + - [x] **T-204** Quote freshness rules — `a1b2c3d`
   ```
4. A commit cannot contain its own hash, so the tick goes in a **separate follow-up commit**, made immediately:
   `T-204 docs(tasks): mark T-204 done (a1b2c3d)`
   Changing only `TASKS.md` in that commit is fine. Never `--amend` the delivering commit to add the tick, because that changes its hash.
5. If a task needed several commits, record **the last one** (the commit after which all acceptance criteria hold), e.g. `` — `a1b2c3d` (3 commits) ``.
6. Partially done work stays `[ ]`. If you stop mid-task, add an indented note: `  - ⏸ WIP on branch t-204-quote-freshness: X done, Y missing`.
7. A task that turns out to be unnecessary is struck through with a reason, never deleted:
   `- [x] ~~**T-312** …~~ — dropped: superseded by T-318`.
8. New work discovered mid-task becomes a **new task** in the right milestone with the next free ID. Do not silently widen the current task.
9. Never tick a task whose acceptance criteria are not all verified. "Compiles" is not "done".

### A2. Definition of Ready (before starting a task)

- [ ] Its dependencies (`Depends:`) are ticked.
- [ ] The referenced `SYSTEM_DESIGN.md` sections have been read.
- [ ] Any `OPEN QUESTION` the task touches is either answered or explicitly covered by the documented default in §26.

### A3. Definition of Done (every task)

- [ ] All acceptance criteria are met and verified by an automated test where feasible. Otherwise, the manual verification steps are written in the commit body.
- [ ] `dotnet build` passes with 0 warnings. `dotnet test Astryx.slnx` and `pnpm typecheck` pass. `pnpm lint` passes once T-003 lands.
- [ ] No secrets, `.env`, `bin/`, `obj/` or `node_modules/` committed.
- [ ] If behaviour diverges from `SYSTEM_DESIGN.md`, the doc is updated **in the same commit** (and §25 gets an ADR row for any changed `DECIDED` item).
- [ ] Public API changes are reflected in `openapi.json` and the regenerated `@astryx/api-client` (from T-010 onward).
- [ ] The tick and hash are recorded per A1.

### A4. Git workflow

- `main` is always green and deployable. Work on short-lived branches: `t-204-quote-freshness`.
- Rebase on `main` before merging. Squash only if the squashed commit keeps the task ID. Record the hash **as it lands on `main`**.
- Commit types: `feat`, `fix`, `refactor`, `test`, `docs`, `chore`, `build`, `ci`, `perf`.
- Commit body explains **why**. Breaking API changes include `BREAKING CHANGE:`.
- Never force-push `main`. Never rewrite history that has been recorded in this file.

### A5. Non-negotiable product and safety rules (from SYSTEM_DESIGN.md)

1. **Real-money trading stays disabled.** No code path may submit an order to Alpaca **Live**. Alpaca **Paper** is allowed. `Features:LiveTrading=true` must keep failing startup until every M8 gate (SYSTEM_DESIGN.md §17.9) is ticked.
10. **Alpaca credentials are secrets.** API key IDs and secrets are envelope-encrypted at rest, decrypted only inside `AlpacaApiClient`, never logged, never stored on the device, and never sent anywhere except Alpaca. The app never calls funding, transfer, journal or Broker API endpoints.
2. **AI is advisory only.** The `Ai` module must never reference `Paper`, Portfolio write ports or `IBrokerAdapter`. AI-suggested levels pass `LevelValidator` and the risk engine before they can seed a proposal.
3. **The risk engine is authoritative and has no override**: not in the UI, not in the API, not behind a flag. It runs again inside the execution transaction.
4. **The only order path** is Proposal → server assessment → Confirm sheet → 1.2 s hold → `POST /paper/orders` with an `Idempotency-Key`.
5. **No floating point for money, price, quantity or percent**, in C# (`decimal`) or TypeScript (decimal strings, `decimal.js-light` for display previews only). JSON carries these values as strings.
6. **Research never looks executed.** Amber, dashed and "Not executed" for research. Green, solid and an order ID only for fills.
7. **Fail closed.** Stale data, a lost connection, an unavailable scanner or a failed check disables approval.
8. **Every user-owned query is scoped to the current user.** A foreign ID returns 404.
9. **Stale or missing data is never interpolated or faked.** Sample data in the UI is labelled "Illustrative".

### A6. Engineering rules

- **Module boundaries:** modules reference only `SharedKernel` and `Contracts`. Cross-module calls go through `Astryx.Contracts` interfaces. The architecture tests must stay green.
- **Backend style:** `Result<T>` for expected failures (no exceptions as control flow); explicit handlers (no MediatR/AutoMapper); `TimeProvider` for time (never `DateTime.Now`); RFC 7807 errors with a stable `code`.
- **Database:** every change is an EF migration in the owning module's schema. Migrations are reviewed SQL, never edited after they land on `main`. Money is `numeric`, time is `timestamptz` UTC, keys are UUIDv7.
- **Tests:** Postgres tests use Testcontainers (no in-memory provider). Time-dependent tests use `FakeTimeProvider`. External providers use fakes or WireMock. Golden files for quant.
- **Mobile:** native components only (no WebView). Add Expo packages with `npx expo install` from `apps/mobile`. Use tokens from `@astryx/design-tokens`, never hard-coded colours or sizes. Money-moving mutations have `retry: 0`.
- **Dependencies:** pinned versions (central NuGet versions, pnpm lockfile). New dependencies are justified in the commit body.
- **Logging:** structured, correlation ID on every line. Never log passwords, tokens, keys, prompts or AI outputs.
- **Docs:** `SYSTEM_DESIGN.md` is updated with any architectural change. User-facing setup changes go into `README.md`.

### A7. Task format

```
- [ ] **T-NNN** Title — <hash goes here when done>
  - **Story:** As a <user>, I want <capability> so that <outcome>.     (user-facing tasks)
  - **Scope:** what is built.                                          (technical tasks)
  - **Acceptance:** verifiable conditions.
  - **Refs:** SYSTEM_DESIGN.md sections · **Depends:** task IDs
```

Priorities: **P0** = required for the end-to-end MVP · **P1** = required before sharing with testers · **P2** = later.

---

## Part B — Milestones

| Milestone | Outcome | Exit criterion |
|---|---|---|
| **M0** Baseline | Skeleton committed, CI green | CI passes on `main` |
| **M1** Foundation | Users can sign up, sign in, and see the themed app shell | Auth journey works on the simulator |
| **M2** Market data | Search, quotes, charts, watchlist on fake data | NVDA stock screen renders design data |
| **M3** Quant and scanner | Deterministic indicators and ranked setups in Discover | Discover shows NVDA #1, score 87 |
| **M4** AI research | Async AI analysis with validated output | NVDA analysis completes via Ollama |
| **M5** Paper trading | Proposal → risk → hold-to-approve → fill → position (internal simulator) | Design NVDA trade fills: 76 sh @ 186.52 |
| **M5b** Alpaca integration (Paper) | Connect an Alpaca paper account and trade it through the same proposal → hold → fill flow | NVDA proposal fills in Alpaca Paper; position, bracket stop/target and P&L reconcile |
| **M6** Portfolio and activity | Positions, P&L, Activity, notifications, strategy lab, settings | Full 10-step journey works manually |
| **M7** E2E and hardening | Automated journey, failure tests, observability, release gate | §21.10 release gate passes |
| **M8** Alpaca Live (gated) | Real-money trading through Alpaca, only after legal, operational and safety gates | Every §17.9 gate ticked, owner sign-off |

**Priority (owner, 2026-11-12): a working local app first.** Work runs M2 → M3 → M4 → M5 → M5b → M6 on the local stack (Docker, simulator, dev build). The CI, scanning and lint tasks (T-001, T-002, T-003, the CI check in T-010) and release or deployment work are deferred until the end-to-end journey works locally. A `Depends:` on one of them does not block local work. T-010's local generation (`pnpm api:gen`) is done with the market endpoints (T-045). Rule A.1 (real money disabled) and every safety rule still apply.

---

## Part C — Backlog

### M0 · Baseline

- [x] **T-000** Commit the verified project skeleton — `1b2a253`
  - **Scope:** first commit containing the skeleton, `SYSTEM_DESIGN.md`, `README.md`, `TASKS.md` and the design PNGs. Configure the git author identity first.
  - **Acceptance:** `git log` shows one commit. `git status` is clean. `.env` is not tracked. `dotnet test` (32 tests) and `pnpm typecheck` pass on a fresh clone.
  - **Refs:** §4.5 · **Depends:** — · **P0**
- [ ] **T-001** CI pipeline (GitHub Actions)
  - **Scope:** a workflow on PR and on push to `main`: setup .NET 10 and pnpm, `dotnet build` (warnings as errors), `dotnet test` (Testcontainers on the Ubuntu runner), `pnpm install --frozen-lockfile`, `pnpm typecheck`, `expo-doctor`. NuGet and pnpm caching.
  - **Acceptance:** a PR with a failing test is blocked. A green run takes under 10 minutes.
  - **Refs:** §21.2, §22.2 · **Depends:** T-000 · **P0**
  - ⏸ WIP: `.github/workflows/ci.yml` runs on github.com/alexanderalkhazov/astryx. Green since `cb41771`; the mobile job had failed on a fresh clone until then. A green run takes **14 m 45 s**, over the 10-minute target. The backend job is ~14 min, almost all integration tests (one Postgres container per test class). **Open:** (1) the owner must enable branch protection on `main` requiring both jobs, before "a PR with a failing test is blocked" can be verified; (2) the run time needs sharing containers across test classes, or splitting the integration tests into parallel jobs.
- [x] **T-002** Secret and dependency scanning in CI
  - **Scope:** gitleaks; `dotnet list package --vulnerable`; `pnpm audit --prod` (fail on high or critical); Dependabot/Renovate config.
  - **Acceptance:** a committed fake secret fails CI. A weekly update PR is opened automatically.
  - **Refs:** §18.8, §18.14 · **Depends:** T-001 · **P1**
- [x] **T-003** Linting and formatting
  - **Scope:** ESLint (Expo config, plus a rule forbidding `color.gain` outside P&L/executed components) and Prettier for TS; `dotnet format --verify-no-changes`; `pnpm lint` in Turbo and CI.
  - **Acceptance:** CI fails on unformatted code. The lint rule catches a deliberate violation.
  - **Refs:** §8.6, §4.4 · **Depends:** T-001 · **P1**
- [x] **T-004** Banned floating-point API analyzer — `bb71a18`
  - **Scope:** `Microsoft.CodeAnalysis.BannedApiAnalyzers` with `BannedSymbols.txt` banning `double`/`float` arithmetic helpers for money in Domain code, with the documented exception for `Volatility()`.
  - **Acceptance:** introducing `double` in a module's `Domain/` folder fails the build.
  - **Refs:** §20.1 · **Depends:** T-000 · **P1**

### M1 · Foundation

**Backend platform**

- [ ] **T-010** OpenAPI → TypeScript client generation
  - **Scope:** export `openapi.json` at build (`Microsoft.Extensions.ApiDescription.Server`), and generate types and Zod schemas into `@astryx/api-client`. A `pnpm api:gen` script. CI checks the generated code is up to date.
  - **Acceptance:** changing a DTO without regenerating fails CI. `SystemStatus` comes from generated types.
  - **Refs:** §8.8, §11 · **Depends:** T-001 · **P0**
  - **Note (T-033):** until this lands, `packages/api-client/src/auth.ts` holds hand-written Zod schemas for the Identity endpoints. Replace them with the generated ones.
  - ⏸ Local part done (see commit with `pnpm api:gen`): OpenAPI export, generated types, Zod schemas checked against them with `satisfies` (decimal-string schema transformer in `apps/api/OpenApi`). Missing: the CI step that fails on drift, deferred with T-001.
- [x] **T-011** SharedKernel primitives — `a1174af`
  - **Scope:** `Money`, `Price`, `Quantity`, `Percent`, `Currency`, `Result<T>`, `Error`, UUIDv7 `Ids`, `RoundingPolicy` (ledger half-even to cents; tick rounding).
  - **Acceptance:** unit tests cover currency mismatch, rounding at boundaries, negative or zero price rejection, and decimal-string JSON converters (round-trip `"186.50"`).
  - **Refs:** §9.5, §20.1 · **Depends:** T-000 · **P0**
- [x] **T-012** Persistence foundation — `db9252f`
  - **Scope:** EF Core + Npgsql, a base `ModuleDbContext` (schema per module, snake_case, UTC conventions, `xmin` concurrency), a migrations runner (`--migrate` flag and dev auto-migrate), and the `citext`/`pg_trgm` extensions.
  - **Acceptance:** an empty migration per schema applies in compose. `/health/ready` reports pending migrations as Unhealthy.
  - **Refs:** §10.1, §22.1 · **Depends:** T-011 · **P0**
- [x] **T-013** Request pipeline essentials — `76db047`
  - **Scope:** correlation-ID middleware, the RFC 7807 exception handler with the `code` and `traceId` extensions, the FluentValidation integration, and a JSON options setup (unknown properties rejected, 64 KB body limit).
  - **Acceptance:** API tests assert the problem-details shape for 400, 404 and 422, and the `X-Correlation-Id` echo.
  - **Refs:** §9.2, §11.1–11.2, §18.7 · **Depends:** T-011 · **P0**
- [x] **T-014** Transactional outbox and dispatcher — `2132c5a`
  - **Scope:** the `infra.outbox_messages` table, `IOutbox.Add(event)` in the same transaction, `OutboxDispatcher` BackgroundService with in-process handlers, dedupe by `message_id`, poison handling.
  - **Acceptance:** an integration test shows that an event is delivered exactly once to an idempotent handler after a simulated crash before dispatch.
  - **Refs:** §9.4, §19.2 · **Depends:** T-012 · **P0**
- [x] **T-015** Postgres job queue, scheduler and reaper — `dc7b491`
  - **Scope:** `infra.job_queue`, a `SKIP LOCKED` claim, leases, the reaper, `dedupe_key` scheduling with `PeriodicTimer`, retry with backoff, `dead` state, `Workers:Enabled` switch.
  - **Acceptance:** tests cover a concurrent claim by 2 workers (one wins), lease expiry requeue, dedupe preventing a double schedule, and moving to dead after max attempts.
  - **Refs:** §19.1–19.3 · **Depends:** T-012 · **P0**
- [x] **T-016** Idempotency infrastructure — `744b7c5`
  - **Scope:** `infra.idempotency_records`, an `[Idempotent]` endpoint filter (in-progress / replay / key-reused semantics), the `Idempotent-Replayed` header, 24 h cleanup.
  - **Acceptance:** IDEM-01, IDEM-02 (10 concurrent requests) and IDEM-03 pass against a test endpoint.
  - **Refs:** §11.1, §20.10, §21.7 · **Depends:** T-013 · **P0**
- [x] **T-017** Audit module — `1f35a80`
  - **Scope:** the `audit.audit_logs` table (monthly partitions), an `IAuditLog` writer usable in-transaction and via the outbox, and redaction of before/after payloads.
  - **Acceptance:** writing an audit row inside a rolled-back transaction leaves no row. Redaction strips `password`/`token` keys (test).
  - **Refs:** §7.18, §10.4 · **Depends:** T-014 · **P0**

**Identity and profiles**

- [x] **T-020** Account registration and email verification (API) — `db2845f`
  - **Story:** As a new user, I want to create an account and verify my email so that my workstation is private to me.
  - **Acceptance:** endpoints A1–A3. Passwords are hashed with the configured iterations. A single-use, hashed verification token expires in 24 h and is emailed via Mailpit as an `astryx://verify-email` link. Registration creates the profile, default risk settings, default watchlist and a paper portfolio ($100,000.00 `initial_deposit` ledger entry) via outbox events. No user enumeration.
  - **Refs:** §7.1, §11.3, §18.1–18.2 · **Depends:** T-013, T-014, T-017 · **P0**
- [x] **T-021** Login, JWT and refresh-token rotation (API) — `1c38fd2`
  - **Story:** As a user, I want to stay signed in securely on my phone so that I don't re-enter my password every time.
  - **Acceptance:** A4–A7. ES256 JWT for 15 min with a `kid`. An opaque 30-day refresh token, stored hashed and family-bound per device. Reuse detection revokes the family and writes an audit row. Lockout after 5 failures. Security-stamp check. Every test in §21.6 passes.
  - **Refs:** §18.3, §6.B · **Depends:** T-020 · **P0**
- [x] **T-022** Password reset and change (API) — `b305824`
  - **Acceptance:** A8, A9 and A12. 30-minute single-use tokens. A reset revokes all sessions and sends a "password changed" email. Responses never reveal whether an email exists.
  - **Refs:** §18.4 · **Depends:** T-021 · **P1**
- [x] **T-023** Current user, profile and risk settings (API) — `354fdf0`
  - **Acceptance:** A10, A11, S1, S2 and S4 with bounds validation (§14.3), ETag/If-Match, and the `RiskSettingsChanged` event. S5 with `mode:"live"` returns 403 `live_trading_disabled` and is audited.
  - **Refs:** §7.2, §11.3, §14.3 · **Depends:** T-021 · **P0**
- [x] **T-024** Authorization and data-isolation guardrails — `01dfc45`
  - **Scope:** `ICurrentUser`, EF global query filters on user-owned entities, the `Authenticated` policy, and the "foreign ID returns 404" convention. Also an auto-generated **isolation test matrix** that walks every route with an `{id}` parameter.
  - **Acceptance:** the matrix runs in CI and fails when a new endpoint leaks another user's data.
  - **Refs:** §18.5, §21.6 · **Depends:** T-021 · **P0**
- [x] **T-025** Rate limiting policies — `2f90540`
  - **Acceptance:** `auth-strict`, `default`, `assessment`, `orders`, `analysis` and `sync` policies from §18.10 are configured. Tests assert 429 with `Retry-After` on auth.
  - **Refs:** §18.10 · **Depends:** T-021 · **P1**

**Mobile foundation**

- [x] **T-030** Fonts, theme and base primitives — `4080bfe`
  - **Scope:** load Geist and Geist Mono via `expo-font`; tabular-number text components (`Text.Data`, `Text.Caption`, …); formatting utilities for decimal strings (`formatMoney`, `formatPct`, `formatPrice`, signed P&L with a real minus sign); `decimal.js-light` preview helpers.
  - **Acceptance:** unit tests show `formatMoney("-630.8")` returns `"−$630.80"`, P&L always carries a sign, and no `Number()` is applied to money strings in formatters.
  - **Refs:** §8.5, §20.1 · **Depends:** T-000 · **P0**
- [x] **T-031** Component library, part 1 (layout and controls) — `f72044e`
  - **Scope:** `Button` (primary, secondary, destructive, link, icon), `Badge`, `Chip`/`FilterChip`, `Segmented`, `Tabs`, `Stepper`, `Switch`, `Input`, `Select`, `StackHeader`, custom `TabBar` (5 tabs, colour-only active state, count badge), `Skeleton`, `Spinner`, `EmptyState`.
  - **Acceptance:** a dev-only `/dev/gallery` route renders every variant and visually matches `docs/design/png/Component library.png`. RNTL tests cover disabled and pressed states.
  - **Refs:** §8.6 · **Depends:** T-030 · **P0**
- [x] **T-032** Component library, part 2 (overlays and status) — `1800650`
  - **Scope:** `StatusBanner` (pending, executed, error, rejected as a discriminated union: executed requires an `orderId`, pending forbids it), `CheckRow`, `Metric` (Hero with dimmed cents, Tile, Grid, ScoreBreakdownBar), `Sheet` (via `@gorhom/bottom-sheet`, portal at root), `Dialog`, `Toast` host (3.8 s, max 3 queued), `useSheet` store.
  - **Acceptance:** type tests prove a `StatusBanner` with tone executed and no `orderId` does not compile. The sheet respects the `dismissible:false` lock.
  - **Refs:** §8.4.13, §8.6 · **Depends:** T-031 · **P0**
- [x] **T-033** API client runtime and session management — `9ceaaa0`
  - **Scope:** `apiFetch` with base URL, Authorization, correlation ID and problem-details parsing; single-flight refresh on 401; session store (Zustand, access token in memory); refresh token and device ID in SecureStore; logout clears the query cache.
  - **Acceptance:** tests show concurrent 401s trigger exactly one refresh, and a refresh failure routes to sign-in with a "Session expired" toast.
  - **Refs:** §8.7–8.9 · **Depends:** T-010, T-021 · **P0**
  - **Note:** built ahead of T-010 (blocked on T-001) using hand-written auth schemas. `createApiClient` in `packages/api-client` is injected with token, refresh and expiry hooks by `apps/mobile/src/lib/api.ts`.
- [x] **T-034** Auth screens and auth gate (PROPOSED screens) — `2ba3de2`
  - **Story:** As a new user, I want to sign up, verify my email from the link and sign in, all within the app's visual language.
  - **Acceptance:** sign-in, sign-up, verify-email (deep link), forgot and reset password are built with RHF and Zod, with server 422 errors mapped to fields. `(app)` redirects to `(auth)` without a session. Deep links are stashed and replayed after sign-in. **Manual check:** a full register → Mailpit link → sign-in on the simulator.
  - **Refs:** §8.3, §8.10 · **Depends:** T-032, T-033 · **P0**
  - **Manual check (2026-11-11):** a user was registered through the API, its Mailpit link was opened in Expo Go on the iPhone 17 simulator (screen: "Email verified"), and the API then accepted a login for that user. A signed-out deep link (`stock/NVDA`) landed on Sign in. The sign-in form was not typed through at first, because no simulator UI automation was installed. On 2026-11-12 `e2e/sign-in.yaml` (Maestro) signed in through the form on the device, and the stashed `stock/NVDA` link was replayed. A cold relaunch then restored the session.
- [x] **T-035** App shell to design fidelity — `54715df`, `585490a`
  - **Scope:** root providers, safe areas, iOS push and Android fade-through transitions, edge-swipe back, Android back closes sheets first, Home header (search, alerts, settings icons), PAPER chip component, the "Illustrative sample data" caption component, and Reduce Motion handling.
  - **Acceptance:** navigating every placeholder route works, and the tab bar matches the PNG design.
  - **Refs:** §8.3, §8.12 · **Depends:** T-032 · **P0**
  - **Manual check (2026-11-12):** `e2e/shell.yaml` walked all five tabs, a pushed screen and Search on the iPhone 17 simulator. The tab bar is about 85 pt against the PNG's 83 pt, the active tab is white with the others grey, and Discover shows scan corners around a line. Home matches 01 · Home: bare header icons, PAPER chip, sample-data notice on one line.
- [x] **T-036** Development build — `905706e`
  - **Scope:** a working `npx expo run:ios` dev client (needed for Skia, bottom-sheet and haptics outside Expo Go), and README instructions including `xcode-select`.
  - **Acceptance:** the dev client launches on the simulator and loads the app from Metro.
  - **Refs:** §4.5 · **Depends:** T-031 · **P0**
  - **Manual check (2026-11-12):** `npx expo run:ios --device "iPhone 17"` built with 0 errors, installed `app.astryx.mobile` and loaded the app from Metro. `e2e/sign-in.yaml` and `e2e/shell.yaml` passed on the dev client, including the header bell, settings and Search close that Expo Go's floating button blocked. A Mailpit `astryx://verify-email` link opened the app directly and verified the email. `expo-dev-client` is held at 58.0.18 by pnpm's `minimumReleaseAge`: 58.0.19 and Expo's other 2026-11-11 patches are too new, so expo-doctor reports patch mismatches until `npx expo install --fix` can take them.

### M2 · Market data

- [x] **T-040** Market schema and instrument master — `9fbe123`
  - **Acceptance:** migrations for `instruments`, `quotes`, `price_bars`, `corporate_actions`, `fundamentals`, `news_articles`, `instrument_news`, `trading_calendar` and `provider_sync_state`, with every constraint and index from §10.4. Constraint tests cover OHLC sanity and unique `(symbol, mic)`.
  - **Refs:** §10.4 (market) · **Depends:** T-012 · **P0**
- [x] **T-041** `IMarketDataProvider` and the deterministic fake provider — `783e97e`
  - **Scope:** the interfaces from §16.1; `FakeMarketDataProvider` and `FakeNewsProvider` backed by `tests/fixtures/market` (a universe of about 1,034 symbols, 300+ daily bars, NVDA tuned to the design: 186.40 / +3.42% / O 180.60 H 186.77 L 180.05 / V 412.8M); seeded intraday random walk; fault injection (down, stale, bad tick).
  - **Acceptance:** the same seed produces identical outputs. Fault modes can be toggled at runtime via `/dev/faults` (Development only).
  - **Refs:** §16.1, §11.11, §21.1 · **Depends:** T-040 · **P0**
- [x] **T-042** Ingestion jobs — `8a84087`
  - **Acceptance:** `InstrumentSync`, `CalendarSync`, `EodBarSync`, `QuoteSync` (priority order: positions, then working orders, then watchlist, then the rest), `FundamentalsSync` and `NewsSync` are scheduled via the job queue with an idempotent upsert. A rate-budget limiter is respected. `QuoteUpdated` events are emitted.
  - **Refs:** §16.3, §19.2 · **Depends:** T-015, T-041 · **P0**
- [x] **T-043** Freshness and market session service — `4571583`
  - **Acceptance:** freshness budgets (§20.5) are applied. `asOf`/`stale` appear on every market response. NodaTime calendar handles DST and half days. `GET /market/session` exists. Tests use `FakeTimeProvider` across the DST switch and a half day.
  - **Refs:** §20.3–20.5 · **Depends:** T-042 · **P0**
- [x] **T-044** Corporate action adjustments
  - **Acceptance:** split-only and total-return adjustment modes over unadjusted storage. Indicator cache is invalidated on a new action. Tests cover a 4:1 split and a dividend.
  - **Refs:** §20.7 · **Depends:** T-040 · **P1**
- [x] **T-045** Market endpoints — `12eb01e`
  - **Acceptance:** M4–M11 with the example payloads from §11.4, decimal strings, `levels` included on bars when a signal exists, and 503 `market_data_unavailable` on provider outage.
  - **Refs:** §11.4 · **Depends:** T-043 · **P0**
  - **Note:** M7 (indicators) ships with the Quant engine task, which owns it.
- [x] **T-046** Search with recents and tags — `a0745ce`
  - **Story:** As a user, I want to search tickers and companies from any header, with recent searches and "Held" or signal tags.
  - **Acceptance:** M1–M3 (ticker prefix ranks above trigram name match). The Search full-screen modal (design 19) uses a 200 ms debounce and empty and error states. Searching "am" returns AMZN and AMD.
  - **Refs:** §8.4.12, §11.4 · **Depends:** T-045, T-035 · **P0**
- [x] **T-047** Watchlists (bc574fd)
  - **Story:** As a user, I want to star a stock to follow it, and manage my watchlist from Settings.
  - **Acceptance:** W1–W7. Star toggle is optimistic with rollback. A default list is created at registration. The PROPOSED `settings/watchlist` screen supports remove and reorder.
  - **Refs:** §7.5, §11.5 · **Depends:** T-045, T-024 · **P0**
- [x] **T-048** CandleChart and LineChart (Skia) (9b7ebf3)
  - **Scope:** price pane 210, volume 40, MA20 and MA50 overlays, entry, stop and target level lines with gutter tags, scrub that snaps to a bar (OHLCV readout, haptic throttled to 30 ms, release returns to the last bar), and an accessibility summary plus a last-10-bars list. `LineChart` with a benchmark dashed overlay and scrub readout.
  - **Acceptance:** 60 fps scrub on the simulator with 250 bars. Visually matches design 04.
  - **Refs:** §8.4.3, §8.6 · **Depends:** T-036 · **P0**
- [x] **T-049** Stock analysis screen: price, chart, tabs shell (a3a3ac4)
  - **Story:** As a user, I want to open a stock and see its price action with moving averages and volume.
  - **Acceptance:** header (back, ticker, star, search), price-xl, signal ribbon (placeholder until M3), OHLCV readout, timeframes 1D/5D/1M/3M/1Y, overlay toggles, Fundamentals and News tabs (design 16), loading, empty and stale states. The bottom button adapts (Add to watchlist for now).
  - **Refs:** §8.4.3 · **Depends:** T-048, T-047 · **P0**
- [x] **T-050** Connection-lost and stale banners (db774e9)
  - **Story:** As a user, I must never mistake stale numbers for live ones.
  - **Acceptance:** NetInfo plus `system.status` polling drives the Home red banner (design 22: "Values are from the last sync at HH:MM ET… Approvals are disabled · Retry"). Fake-provider fault injection toggles it within 90 s.
  - **Refs:** §8.11, §20.5 · **Depends:** T-043, T-035 · **P0**
- [ ] **T-051** Market data provider decision (ADR)
  - **Scope:** evaluate at least 2 real providers against the §16.2 matrix (coverage, history, rate limits, licence for local storage and display, cost) and record an ADR. **Does not implement the adapter.**
  - **Acceptance:** an ADR in `docs/adr/` with sources, plus Q-03 and Q-04 answered or explicitly deferred by the owner.
  - **Refs:** §16.2, Q-03, Q-04 · **Depends:** — · **P1**
  - Note (2026-11-17): the owner chose Alpaca for real data and it is implemented (T-147). The evaluation of a second provider and the Q-04 licence answer are still open.

### M3 · Quant engine and scanner

- [x] **T-060** Indicator library with golden tests (fafad94)
  - **Acceptance:** SMA, EMA (SMA seed), RSI (Wilder), MACD 12/26/9, ATR (Wilder), relative volume, ROC, volatility (the single documented `double` log step), relative-strength rank and trend classification. Golden files generated by `tools/gen_golden.py` match at 1e-6. Property tests from §13.6 pass. Insufficient history returns `Insufficient`, never partial values.
  - **Refs:** §13.2–13.6 · **Depends:** T-011 · **P0**
- [x] **T-061** Indicator snapshot service and endpoint (d2078f2)
  - **Acceptance:** `IStockAnalysisEngine.Analyze`, the `indicator_snapshots` cache keyed by `params_hash`, data-quality flags (`ok`, `gaps`, `stale`, `insufficient`), and M7 matching the §11.4 example.
  - **Refs:** §13.5, §11.4 · **Depends:** T-060, T-045 · **P0**
- [x] **T-062** RSI and MACD panes plus the Technicals tab (265cad0)
  - **Acceptance:** RSI pane 64 with 30/70 guides, MACD pane 64 with histogram, an `IndicatorsSheet`, and a Technicals table with `as of` (design 05).
  - **Refs:** §8.4.3 · **Depends:** T-061, T-048 · **P0**
- [x] **T-063** Strategies catalog and versioning (ba77a72)
  - **Acceptance:** `strategy.*` migrations. The five design strategies are seeded (Base Breakout v3, Trend Momentum, Trend Pullback, RSI(2) Mean Reversion, Earnings Drift) with immutable published versions and parameter schemas. ST1, ST3 and ST5 return seeded data.
  - **Refs:** §7.8, §10.4 (strategy) · **Depends:** T-012 · **P0**
- [x] **T-064** Base Breakout rule (first `IStrategyRule`) (5a85dfd)
  - **Scope:** a deterministic rule from the §8.4.9 parameters (base length 6 wk, volume multiple 2.2×, min RS 80, stop 1.5× ATR) producing entry, stop, target, a reason string and component scores. The rule definition is documented in `SYSTEM_DESIGN.md` (partly answers Q-09).
  - **Acceptance:** NVDA fixture produces buy_stop 186.50 / stop 178.20 / target 204.00 / score 87 and the reason "Cleared a 6-week base at $185.90 on 2.4× average volume."
  - **Refs:** §13.4, Q-09 · **Depends:** T-060, T-063 · **P0**
- [x] **T-065** Scanner runs and signals (eceafd0)
  - **Acceptance:** `discovery.*` migrations. `ScannerRunJob` after EOD and every 15 min in session. Universe filters from Settings (min price $10, avg $ volume $20M). Signals with score breakdown. Superseding of old signals. `SignalDetected` event. `CompletedWithWarnings` and `failed` states with `SCN-xxx` codes.
  - **Refs:** §7.4, §19.2 · **Depends:** T-064, T-042 · **P0**
- [x] **T-066** Scanner and signal endpoints (e677bde)
  - **Acceptance:** D1–D4 with filters, sorts, counts per chip and cursor pagination. 503 `scanner_unavailable` when the latest run failed or is older than two intervals.
  - **Refs:** §11.4 (scanner) · **Depends:** T-065 · **P0**
- [x] **T-067** Discover screen with filters and states (9817c2f)
  - **Story:** As a user, I want a ranked list of today's setups that I can narrow by signal type, sector, market cap, timeframe and score.
  - **Acceptance:** scanner status line, signal chips with counts, removable active filter chips, `OpportunityRow` list (FlashList), `FiltersSheet` with a live "Show N setups" count, filters persisted per user. Loading skeletons (5 rows, staggered 120 ms), empty state (Reset filters / Adjust), error state ("Scanner feed unavailable", signals hidden, code, Retry now), pull to refresh. The Discover tab badge shows the count. Matches designs 02, 03, 20, 21 and 23.
  - **Refs:** §8.4.2 · **Depends:** T-066, T-032 · **P0**
- [x] **T-068** Signal tab: evidence, score breakdown, invalidation (9ff6e10)
  - **Story:** As a user, I want to understand *why* a setup was flagged before I consider trading it.
  - **Acceptance:** the Stock › Signal tab shows a headline, summary, evidence, `ScoreBreakdownBar`, invalidation (deterministic template copy) and the "No active signal · Evaluated HH:MM ET" state. The bottom bar shows "Entry · R:R · Research only · not executed".
  - **Refs:** §8.4.3, §13.4 · **Depends:** T-066, T-049 · **P0**

### M4 · AI research

- [x] **T-070** `IAiProvider` for Ollama with a capability probe
  - **Acceptance:** `OllamaAiProvider` (`/api/chat`, `stream:false`, `think:false`) sends `format` **only** if a startup probe shows support (`qwen3.5:0.8b` → unsupported, per §4.5). Model info and digest come via `/api/show`. A circuit breaker is configured. `FakeAiProvider` covers the fixture scenarios (valid, invalid JSON, schema error, hallucinated numbers, timeout, 500).
  - **Refs:** §12.3, §4.5 · **Depends:** T-013 · **P0**
- [x] **T-071** Prompt registry and context builder
  - **Acceptance:** versioned prompts in `src/Modules/Ai/Prompts/<id>/<version>/`. The active version comes from config. `AnalysisContextBuilder` produces `ctx.v1` (20 bars, indicators, signal, fundamentals, ≤ 10 sanitised news items in an untrusted `<news>` block, price band). It fails early on stale or insufficient data without calling the model.
  - **Refs:** §12.4–12.5 · **Depends:** T-061, T-070 · **P0**
- [x] **T-072** Output validator and LevelValidator
  - **Acceptance:** all 8 validation steps from §12.7 (parse with fence strip and one repair retry, schema `analysis.v1`, identity, temporal, numeric grounding, levels via ATR/band/R:R, content policy, forbidden fields). The backend computes `rewardRisk`. Fixture tests cover every rejection path.
  - **Refs:** §12.6–12.7 · **Depends:** T-071 · **P0**
- [x] **T-073** Analysis jobs, worker and endpoints
  - **Acceptance:** `ai.*` migrations. AI1–AI5 (202 plus `Location`, dedupe by `request_hash`, quota 30/day, one active job per user/symbol/kind). `AiAnalysisWorker` with leases, 3 attempts with backoff, cancellation checkpoints, and `ai_requests` logging (no prompt text in logs). Failure tests from §21.8 pass for "Ollama stopped" and "worker killed mid-job".
  - **Refs:** §12.8–12.9, §19.4 · **Depends:** T-072, T-015 · **P0**
- [x] **T-074** Real-time job updates (SignalR hub)
  - **Acceptance:** hub `/hubs/workstation` with JWT auth and per-user groups. `analysis.updated` is published via the outbox. The mobile SignalR client lifecycle (connect on session, reconnect, fall back to polling).
  - **Refs:** §9.6 · **Depends:** T-073, T-033 · **P0**
- [x] **T-075** "Analyze with AI" in the Signal tab
  - **Story:** As a user, I want an AI-written explanation of bullish, bearish and neutral scenarios, clearly labelled as experimental research.
  - **Acceptance:** button → job progress → rendered scenarios, evidence (supporting and contradicting), risks, catalysts, and confidence with the "not a calibrated probability" note. The "Experimental AI model" label and disclaimer are shown. Failure shows "Analysis couldn't be generated reliably. Indicators are still available." **Manual check:** a real Ollama run for NVDA.
  - **Refs:** §8.4.3, §12.6 · **Depends:** T-074, T-068 · **P0**
- [x] **T-076** AI evaluation harness
  - **Acceptance:** `tools/Astryx.AiEval` runs ≥ 50 golden contexts, including injection headlines, and writes a Markdown and JSON report (schema-valid rate, grounding violations, policy hits, p50/p95 latency). A baseline report for `qwen3.5:0.8b` is committed.
  - **Refs:** §12.10 · **Depends:** T-072 · **P1**

### M5 · Paper trading

- [x] **T-080** Portfolio ledger foundation
  - **Note:** `portfolio.portfolios` (incl. `reserved_cash`) and `portfolio.transactions` already exist from T-020; this task adds positions, the append-only guard and the ledger service.
  - **Acceptance:** remaining `portfolio.*` migrations. `IPortfolioLedger` posts append-only transactions with a unique `idempotency_ref`. Cash cache plus `version` increment. The DB role has no UPDATE or DELETE on `transactions`. A ledger-sum invariant helper is used by tests.
  - **Refs:** §10.4 (portfolio), §15.3, §20.8 · **Depends:** T-012, T-020 · **P0**
- [x] **T-081** Trading proposals
  - **Acceptance:** `trading.proposals` migration. T1–T3 and T5. Created from a signal (deterministic levels) or a validated analysis (levels accepted only). One pending proposal per instrument. Expiry at session end minus 30 min (`ProposalExpiryJob`). The Reject flow keeps the signal for hit-rate audit.
  - **Refs:** §7.9, §11.7 · **Depends:** T-066, T-073, T-080 · **P0**
- [x] **T-082** Risk engine
  - **Acceptance:** sizing (min of risk, weight cap and cash; `sized_by`), all 12 checks with pass, warn and fail thresholds, `inputs_hash`, 60 s validity. **RISK-01…RISK-10 pass**, including the exact design numbers (76 sh, $14,174.00, −$630.80, +$1,330.00, R:R 2.11, sized by weight cap).
  - **Refs:** §14 · **Depends:** T-080, T-043 · **P0**
- [x] **T-083** Assessment endpoint
  - **Acceptance:** T4 persists immutable `risk_assessments` matching the §11.7 example. Risk choices above the user's max return 422. A 60/min rate limit. Risk settings changes make existing assessments stale.
  - **Refs:** §11.7, §14.5 · **Depends:** T-082, T-023 · **P0**
- [x] **T-084** Paper execution engine and order endpoint
  - **Acceptance:** `trading.orders` and `trading.fills` migrations. `MockBrokerAdapter`/`PaperFillSimulator` (market, buy-stop, limit; slippage max($0.01, 1 bp) rounded up to the cent; commission $0.00). T6 is implemented exactly as the §15.3 transaction: idempotency, portfolio `FOR UPDATE`, risk re-run on a fresh quote, order, fill, position, OCO exits, ledger, outbox, audit. The `SIM-XXXXXX` ID format. **PAPER-01, PAPER-02 and IDEM-01…05 pass.** The design trade fills at 186.52 with cash 21,840.55 → 7,665.03.
  - **Refs:** §15, §20.9 · **Depends:** T-083, T-016 · **P0**
- [x] **T-085** Position monitor and session jobs
  - **Acceptance:** `PositionMonitor` consumes `QuoteUpdated` and triggers working entries and OCO exits under a per-portfolio lock. A gap-aware evaluation, where the stop wins when both levels trigger in one bar. Stop-proximity alert (≤ 1.5%, once per session). `SessionCloseJob` expires day orders and releases reserved cash. `StartOfDayJob` and the daily loss halt. **PAPER-03…06 pass.**
  - **Refs:** §15.4, §7.13 · **Depends:** T-084, T-042 · **P0**
- [x] **T-086** Close position and cancel order
  - **Acceptance:** T9, T10 and T11 with idempotency. The close preview is computed server-side. **PAPER-07 passes.**
  - **Refs:** §15.5, §11.7 · **Depends:** T-085 · **P0**
- [x] **T-087** Trade proposal screen
  - **Story:** As a user, I want to review entry, stop, target, size and pre-trade checks, adjusting size and risk %, before deciding.
  - **Acceptance:** design 06 fidelity: pending StatusBanner with expiry, BUY · LONG, source link, order type, level tiles, R:R bar, size stepper, risk 0.50/0.75/1.00% (choices above the max disabled), "Sized by …", capital, max loss, profit, fees and spread, portfolio impact, the six checks (plus engine checks when they warn or fail). Each change triggers a debounced (150 ms) server re-assessment that cancels in-flight requests. Approve is enabled **only** when the assessment matches the current inputs, is approvable and not expired. Disabled reasons are shown. The Reject dialog works.
  - **Refs:** §8.4.4 · **Depends:** T-083, T-032 · **P0**
- [x] **T-088** Hold-to-approve, confirm sheet and fill receipt
  - **Story:** As a user, I want approval to be deliberate and impossible to trigger by accident, and to get a clear receipt.
  - **Acceptance:** `HoldToConfirm` (1200 ms linear fill, release resets, success haptic, keyboard Enter/Space, VoiceOver action plus second confirm). `ConfirmOrderSheet` repeats every field (design 07). One Idempotency-Key per sheet session. The sheet is locked while sending. `FillReceiptSheet` (design 08) shows the executed banner, order ID, slippage, OCO and commission, or the "Order working · not filled" variant. Errors unlock the sheet and show a reason toast. RNTL tests: 1199 ms does not fire, 1200 ms fires exactly once.
  - **Refs:** §8.4.5 · **Depends:** T-087, T-084 · **P0**
- [x] **T-089** Order and position real-time events
  - **Acceptance:** `order.updated` and `position.updated` SignalR events invalidate proposal, portfolio, positions and activity queries. Fill and stop/target toasts appear.
  - **Refs:** §9.6 · **Depends:** T-088, T-074 · **P0**

### M5b · Alpaca integration (Paper)

> Execution venue per SYSTEM_DESIGN.md §17 (ADR-023). Every task here runs against the **Paper** environment only (`paper-api.alpaca.markets`, paper keys). Contract tests use WireMock fixtures built from Alpaca's documented examples, so CI never needs real keys. One opt-in smoke suite uses paper keys from CI secrets.

- [x] **T-140** Alpaca API client — `240fdbb`
  - **Scope:** typed `AlpacaApiClient` for the Trading API (`/v2`) with the base URL taken from the connection's environment: `APCA-API-KEY-ID` + `APCA-API-SECRET-KEY` on every call; per-account rate limiter (200 requests/min, backs off on `429`); retries with backoff only for idempotent reads; error mapping (401/403 auth → `broker_credentials_invalid`, 403 buying power → `broker_insufficient_funds`, 422 → `broker_order_invalid`, 429 → `broker_rate_limited`, 5xx → `broker_unavailable`); circuit breaker; redacted logging; no money-movement or Broker API methods exist.
  - **Acceptance:** WireMock contract tests cover auth headers, paper vs live base URL selection, 429, 5xx, timeout and malformed responses. An architecture test bans funding, transfer, journal and Broker API paths. Keys and secrets never appear in logs (log-capture test).
  - **Refs:** §17.3 · **Depends:** T-013 · **P0**
  - Done with T-141: `AlpacaHttp`/`AlpacaTradingClient` (paper host only), per-key token bucket (180/min), error mapping, `AlpacaTradingClientTests` + `AlpacaMarketDataProviderTests` contract tests, `BrokerMoneyMovementTests` architecture test; secrets never in messages (`AlpacaCredentials.ToString` redacts) and log templates guarded by `SensitiveLoggingTests`.
- [x] **T-141** Connect Alpaca (encrypted credentials and Settings card) — `240fdbb`
  - **Story:** As a user, I want to connect my Alpaca paper account with my API keys so the app can see my portfolio and place approved trades.
  - **Acceptance:** `trading.broker_connections` migration (environment, key suffix, permissions, ciphertext). Envelope encryption via Data Protection (KMS in production). N7–N9: keys validated with `GET /v2/account` on the paper base URL (live-only keys rejected); `environment:"live"` returns 403 `live_trading_disabled`. Connecting creates an `alpaca_paper` portfolio; disconnect deletes the ciphertext. PROPOSED "Connect Alpaca" screen (paste key ID and secret, choose Paper, help link to the Alpaca dashboard's paper API keys). The design 18 card shows the masked key ID, sync time, status and permission rows. Audit rows for connect and disconnect.
  - **Refs:** §17.6, §10.4, §8.4.11 · **Depends:** T-140, T-023, T-017, T-032 · **P0**
  - Done (2026-11-17): `trading.broker_connections`, `CredentialVault` envelope encryption (§17.6), N7–N9, Connect Alpaca screen and Settings card, audit rows; `AlpacaBrokerTests`.
- [x] **T-142** Instrument mapping and eligibility — `46b04dc`
  - **Acceptance:** our instruments map to Alpaca assets by symbol (`GET /v2/assets/{symbol}`, `asset_id` cached in `market.instruments.provider_refs.alpaca`). Instruments that are not `tradable` or not `active` show "Not tradable at Alpaca". New risk checks `broker_eligible` (tradable, active), `broker_connected` and `broker_write_permission`, shown only when they warn or fail.
  - **Refs:** §17.3, §14.5 · **Depends:** T-141, T-082 · **P0**
  - Done (2026-11-19): `broker_connected`, `broker_write_permission` and `broker_eligible` checks for Alpaca portfolios (warn when Alpaca can't be asked); assets looked up with `GET /v2/assets/{symbol}` and cached in memory for an hour. Deviation: `asset_id` is not persisted in `provider_refs`, because orders go by symbol and tradability must be re-checked anyway.
- [x] **T-143** `AlpacaBrokerAdapter` (Paper) — `240fdbb`
  - **Scope:** implements `IBrokerAdapter`: submit (`market` / `stop` / `limit` mapping, whole-share `qty`, `time_in_force:"day"`, `order_class:"bracket"` with `stop_loss.stop_price` and `take_profit.limit_price`), order lookup by id and by `client_order_id` with status mapping (`new`/`accepted`/`pending_new`/`partially_filled` in flight; `filled` filled; `rejected`/`canceled`/`expired` closed), close position (cancel bracket legs, then `DELETE /v2/positions/{symbol}`), replace stop and target legs (`PATCH`), cancel, account and positions snapshot.
  - **Acceptance:** contract tests per method using documented request and response shapes. A manual Paper run answers Q-22 (does a bracket accept a `stop` entry? what does a duplicate `client_order_id` return?) and records the answers in §17.3–17.4. If a bracket rejects a stop entry, implement the `oto` fallback.
  - **Refs:** §17.2–17.4, Q-22 · **Depends:** T-140, T-142 · **P0**
  - Done (2026-11-17): bracket submit, lookups by id and client id, close, cancel, account and positions; Q-22 answered by `AlpacaPaperSmokeTests` against Paper (bracket takes a stop entry; duplicate client id → 422). The adapter is `AlpacaExecution` + `IAlpacaTradingClient` rather than a separate `IBrokerAdapter` interface.
- [x] **T-144** Asynchronous broker order flow — `207a1f0`
  - **Story:** As a user, after I hold to approve, I see "Sending to Alpaca…", then a fill receipt with the Alpaca order id, or a clear rejection reason.
  - **Acceptance:** for `alpaca_*` portfolios, T6 commits the order as `submitting` with a cash reservation and returns 202. `SubmitBrokerOrderJob` sends with `client_order_id` = Idempotency-Key, and after a timeout or lost response it **looks up by `client_order_id` before any resubmit**. `LookupBrokerOrderJob` polls until terminal; a fill records mirrored fill, position (`broker_position_id`), ledger and proposal-approved state, then SignalR `order.updated`. A rejection releases cash and returns the proposal to pending with Alpaca's reason. Fill receipt and Activity show Alpaca ids. Tests: a lost response produces no duplicate order (lookup finds it); a reject path; a stuck order alerts after 10 minutes; 10 concurrent submits with the same key produce one Alpaca call.
  - **Refs:** §17.5, §15.9 · **Depends:** T-143, T-084, T-088, T-015 · **P0**
  - Done (2026-11-18): submit/settle jobs, lookup before resubmit, 422-duplicate recovery, fills mirrored at Alpaca's price with bracket legs as exits; a rejection releases cash and returns the proposal to pending; one Alpaca call per order under concurrent approvals and retries; `BrokerOrderStuck` alert after 10 minutes (`AlpacaBrokerTests`).
- [x] **T-145** Broker portfolio sync and reconciliation — `0663b8d`
  - **Acceptance:** `BrokerPortfolioSyncJob` (60 s in session, 15 min otherwise, on pull-to-refresh, after fills) mirrors positions, cash and equity from `GET /v2/account` and `GET /v2/positions`. Positions closed by bracket legs become exit fills with the correct `exit_reason` and realized P&L. `BrokerReconciliationJob` compares against `account/activities` and halts entries on a mismatch. A connection that is degraded (auth failure, 5xx streak or `trading_blocked`) disables approval and shows the Connection-lost banner. Home and Settings show "synced HH:MM ET".
  - **Refs:** §17.7, §20.13 · **Depends:** T-144, T-090 · **P0**
  - Done (2026-11-19): one-minute `BrokerSyncJob` settles entries, mirrors filled bracket legs as stop/target exits, posts cash `adjustment`s when nothing is in flight, compares positions with `GET /v2/positions` and halts entries on a mismatch (`46b04dc`), records `last_sync_at` and marks a rejected key degraded, which fails `broker_connected` and disables approval (T-142); manual close goes through Alpaca. Pull-to-refresh (`POST /portfolio/sync`) syncs the user's account first; outside the session the recurring pass reconciles each account every 15 minutes; Home and Settings show "Alpaca synced HH:MM ET" or "connection lost". Fills are reconciled against `account/activities` (24-hour window) in the same pass.
- [ ] **T-146** Alpaca trading-cost preview
  - **Acceptance:** for `alpaca_*` portfolios, the proposal's "Fees and spread" and `maxLossInclCosts` use Alpaca's commission-free pricing, the quote's spread, and estimated regulatory fees (shown for live, labelled as not simulated in paper). Paper fill receipts say "Simulated by Alpaca". Q-23 is answered or deferred with evidence.
  - **Refs:** §17.3, §14.4, Q-23 · **Depends:** T-143, T-083 · **P1**
- [ ] **T-147** Alpaca market data provider (decision and adapter)
  - **Acceptance:** an ADR resolves Q-03 between Alpaca market data and a dedicated vendor, including the IEX-only free plan versus Algo Trader Plus and whether Alpaca data may be stored and displayed. If Alpaca is chosen: `AlpacaMarketDataProvider` (multi-symbol snapshots and bars, corporate actions) within the plan's rate limit (nightly EOD bars for the universe, batched snapshots for positions, working orders and watchlist).
  - **Refs:** §16, §17.3, Q-03 · **Depends:** T-051, T-140 · **P1**
  - ⏸ WIP (2026-11-17): `AlpacaMarketDataProvider` and `AlpacaNewsProvider` implemented and running locally (500 S&P 500 symbols, 2 years of real daily bars, IEX real-time quotes with delayed consolidated volume, corporate actions, calendar, news); contract tests in `AlpacaMarketDataProviderTests`. **Open:** the ADR resolving Q-03/Q-04 (licence for storage and display) with T-051.
- [ ] **T-148** Alpaca trade-updates streaming
  - **Acceptance:** `TradeUpdatesListener` authenticates to `wss://paper-api.alpaca.markets/stream` and listens to `trade_updates`, so `fill`, `partial_fill`, `rejected`, `canceled` and `expired` events settle orders in near real time (lookups stay as the fallback). Optional: the market data stream feeds `QuoteUpdated` for held and watched symbols. Reconnect with resubscribe. Lag metric.
  - **Refs:** §17.3, §17.7, §9.6 · **Depends:** T-145 · **P2**
- [ ] **T-149** Alpaca Paper end-to-end smoke
  - **Acceptance:** an opt-in CI job (paper keys from secrets, manual trigger) connects, places a 1-share bracket order on a liquid symbol, confirms the fill, verifies the mirrored position, closes it and verifies reconciliation is clean. A Maestro variant of the journey runs against an `alpaca_paper` portfolio.
  - **Refs:** §17, §21 · **Depends:** T-145 · **P1**
  - ⏸ WIP: `AlpacaPaperSmokeTests` (opt-in with `ALPACA_KEY_ID`/`ALPACA_SECRET_KEY`) checks the real client end to end without a fill. **Open:** the CI job and a filled round trip during market hours.

### M6 · Portfolio, activity, strategy lab, settings

- [x] **T-090** Valuation, snapshots and performance
  - **Acceptance:** `ValuationService` (stale flags). `PortfolioSnapshotJob` (hourly and EOD). Benchmark series (SPY proxy in fake data). P1, P2, P5 and P6, with TWR, max DD, win rate and avg win/loss computed from fills and snapshots.
  - **Refs:** §7.12, §7.14, §20.11 · **Depends:** T-085 · **P0**
- [x] **T-091** Allocation, exposure and attention
  - **Acceptance:** P7, P8 (beta-weighted exposure null when beta is missing) and P9 (pending proposals, stop proximity, working orders, halt). Weight drift above the cap is flagged, never auto-sold.
  - **Refs:** §8.4.6, §11.8 · **Depends:** T-090 · **P0**
- [x] **T-092** Portfolio screen
  - **Story:** As a user, I want to see my positions against the 12% cap, my allocation and my performance.
  - **Acceptance:** hero with dimmed cents and PAPER chip. Tabs Positions (`PositionRow`, weight bar vs cap, NEW badge until seen), Allocation (sector bar, warnings above 30%, asset donut, exposure, open risk to stops) and Performance (equity vs benchmark, metrics, closed trades). Matches designs 09 and 15.
  - **Refs:** §8.4.6 · **Depends:** T-091, T-048 · **P0**
- [x] **T-093** Position detail and close flow
  - **Story:** As a user, I want to see a position's progress between stop and target, know which strategy generated it, and close it deliberately.
  - **Acceptance:** design 10: NEW · SIMULATED badge, value, unrealized P&L, 1M line, stop/entry/current/target track, facts (including SIM order ID), "Generated by" card linking to the strategy, action bar (Analysis · Close position). Close dialog (design 24) with "Keep open" as the default focus. Closed state points to Performance.
  - **Refs:** §8.4.7 · **Depends:** T-086, T-092 · **P0**
- [x] **T-094** Home screen complete
  - **Story:** As a user, opening the app shows me what my portfolio did and what needs my attention.
  - **Acceptance:** design 01: hero (value, day P&L, all time), performance chart with timeframes and S&P 500 overlay, scrub readout, Cash/Invested/Unrealized, Needs attention (deep-links two levels), Top opportunities carousel (snap 246), market breadth (M11), open positions, strategy MTD. Pull to refresh (`POST /portfolio/sync`, toast "Synced HH:MM ET").
  - **Refs:** §8.4.1 · **Depends:** T-091, T-067 · **P0**
- [x] **T-095** Activity feed and notifications
  - **Story:** As a user, every fill, alert, strategy event and system issue is in one timeline that links to its source.
  - **Acceptance:** `notify.*` migrations. Outbox handlers create notifications with `deep_link` and dedupe keys. N1–N3 and N6. Activity screen (design 17): filter chips, pending orders (dashed empty card), dated timeline, system status, cancel working order dialog. The bell shows an unread dot.
  - **Refs:** §7.15, §8.4.10, §11.10 · **Depends:** T-089 · **P0**
- [x] **T-096** Strategy lab (read-only)
  - **Story:** As a user, I want to inspect the strategy that generated a trade, including its backtest and out-of-sample results.
  - **Acceptance:** seeded backtest runs. ST1–ST5. Strategy lab list and Compare (designs 13 and 14). Strategy detail tabs Overview, Backtest (OOS band, drawdown, 8 metrics, IS vs OOS, monthly heatmap — design 11) and Configure as a read-only "Coming soon" state (design 12). Copy states that backtests are historical and illustrative.
  - **Refs:** §8.4.8–8.4.9 · **Depends:** T-063, T-048 · **P0**
- [x] **T-097** Drawdown guard
  - **Acceptance:** a rolling 30-day drawdown of each strategy's live paper results. Warn at 90% of the threshold, auto-pause signals at −6.0% (`active_guarded` / `paused`). An Activity "Strategy" event is emitted.
  - **Refs:** §7.8 · **Depends:** T-096, T-095 · **P1**
- [x] **T-098** Settings screen
  - **Story:** As a user, I want to see my paper connection and permissions, adjust risk limits and scanner settings, and understand that live trading is unavailable.
  - **Acceptance:** design 18: broker connection card (from T-141: "Alpaca · paper account", masked key, sync time, permissions incl. "Deposits & withdrawals — Never requested"; "Connect Alpaca" when not connected), Paper/Live segmented with the guard dialog (design 25) ending in "Live trading isn't available yet" (server 403), risk limit steppers with bounds, scanner universe and filters, watchlist link, notification toggles, security (sign out, sign out of all devices, change password), and prototype controls in dev builds only.
  - **Refs:** §8.4.11, §17.3 · **Depends:** T-023, T-095 · **P0**
- [x] **T-099** Paper account reset
  - **Acceptance:** P11 with the `RESET` confirmation, close at last price, cancel orders, `reset_close`/`reset_deposit` entries and a new performance epoch. **PAPER-08 passes.** A PROPOSED confirmation dialog in Settings.
  - **Refs:** §11.8 · **Depends:** T-086, T-098 · **P1**
- [x] **T-100** Reconciliation job
  - **Acceptance:** nightly checks 1–6 from §20.13. A mismatch halts new entries for that portfolio, raises a critical log and alert, and writes an audit row. It runs clean after the whole test suite.
  - **Refs:** §20.13 · **Depends:** T-085 · **P1**
- [x] **T-101** Push notifications
  - **Acceptance:** `expo-notifications` registration (N4/N5). `PushSender` respects preference categories. Tapping a notification deep-links to the same route as its Activity event. Revoked tokens are cleaned up.
  - **Refs:** §7.15 · **Depends:** T-095, T-036 · **P1**

### M7 · End-to-end and hardening

- [x] **T-110** E2E: the 10-step journey (Maestro)
  - **Acceptance:** `tests/e2e/journey.yaml` runs against the compose stack with fake data on the iOS simulator: Home → Discover → filters → NVDA → Signal → proposal → sizing → hold 1.2 s → fill receipt → Portfolio NEW badge → Position → Generated by → strategy backtest. It passes three times in a row.
  - **Refs:** §2.3, §21.9 · **Depends:** T-093, T-094, T-096 · **P0**
- [x] **T-111** E2E: states and failures
  - **Acceptance:** `states.yaml` toggles dev faults and asserts the connection-lost banner, approval disabled, scanner error, empty Discover, a stop-hit toast and the Activity entry.
  - **Refs:** §21.8–21.9 · **Depends:** T-110 · **P0**
- [x] **T-112** Order-path safety proof test
  - **Acceptance:** an API test calls `POST /paper/orders` with every combination of missing, stale, foreign, failed, expired and mismatched-hash assessments, and also with `Features:LiveTrading=true`. Each one is rejected and no order or ledger row is created.
  - **Refs:** §21.10 items 4–5, A5 · **Depends:** T-084 · **P0**
- [x] **T-113** Observability
  - **Acceptance:** OpenTelemetry traces and metrics from §23.4 are exported to a local collector in compose (Grafana/Tempo or Aspire dashboard). Correlation IDs propagate through the outbox and jobs. Sentry is wired for API and mobile (DSN via env, disabled when unset).
  - **Refs:** §23 · **Depends:** T-085 · **P1**
- [x] **T-114** Alerts and runbooks
  - **Acceptance:** alert rules from §23.8, with a runbook entry in `docs/runbooks/` for each (stale market data, monitor lag, reconciliation mismatch, live flag enabled, …).
  - **Refs:** §23.8 · **Depends:** T-113 · **P1**
- [x] **T-115** Security hardening pass
  - **Acceptance:** an OWASP ASVS L1 checklist is completed and recorded in `docs/security/`. Log redaction is verified. JWT key rotation drill. Screenshot protection on balance screens. Postgres row-level security evaluated and an ADR recorded.
  - **Refs:** §18 · **Depends:** T-024, T-113 · **P1**
- [ ] **T-116** Load smoke test
  - **Acceptance:** a k6 script with 50 VUs against assessment and order endpoints. p95 meets NFR-01 and NFR-02. Zero duplicate fills.
  - **Refs:** §2.5, §21.2 · **Depends:** T-084 · **P2**
- [ ] **T-117** Staging deployment
  - **Acceptance:** hosting decision recorded as an ADR (Q-16). Staging environment with managed Postgres, API and worker roles, secrets in a manager, migrations as a job, and a GitHub Actions deploy with an E2E smoke run. Backup and restore drill documented.
  - **Refs:** §22.2, Q-16 · **Depends:** T-110, T-113 · **P1**
- [ ] **T-118** Tester builds and in-app disclaimers
  - **Acceptance:** EAS builds for TestFlight and Play internal testing. Onboarding disclaimer ("Paper trading only · not investment advice · AI research is experimental"). Privacy policy page. App icons and splash in Graphite style.
  - **Refs:** §1, §12.6 · **Depends:** T-117 · **P1**
- [ ] **T-119** MVP release gate
  - **Acceptance:** every item in §21.10 is verified and listed in the commit body with evidence (CI run link, E2E artifacts). Open P0 tasks: none.
  - **Refs:** §21.10 · **Depends:** T-110–T-118 (P0/P1) · **P0**

### M8 · Alpaca Live (gated: no real money until every task is ticked)

> SYSTEM_DESIGN.md §17.9. These tasks are gates as well as work items: the live feature flag must keep failing startup until T-130–T-136 are all `[x]`.

- [ ] **T-130** Legal, regulatory and Alpaca-terms review for real-money automation
  - **Acceptance:** written review for the operator's jurisdiction, covering Alpaca's terms for automated real-money orders (own account with personal keys vs an approved OAuth app for other users), plus a decision recorded as an ADR (Q-02, Q-13). A "no" stops M8.
  - **Refs:** §17.9 · **Depends:** T-149 · **P2**
- [ ] **T-131** Paper soak period
  - **Acceptance:** ≥ 4 consecutive weeks of `alpaca_paper` use with zero reconciliation mismatches, zero duplicate orders and zero unhandled stuck orders, evidenced by metrics and an audit export.
  - **Refs:** §17.9 · **Depends:** T-145 · **P2**
- [ ] **T-132** Live-mode guard and re-authentication
  - **Acceptance:** the live guard flow from design 25 is complete (re-confirm limits, device biometrics, type `LIVE`) plus a server-side recent re-auth (password ≤ 5 min). A per-user allow-list and the per-environment flag are both required.
  - **Refs:** §17.9, §8.4.11 · **Depends:** T-098, T-130 · **P2**
- [ ] **T-133** Real connection and separate real portfolio
  - **Acceptance:** `environment:"live"` connections (separate live keys) are accepted only when gates pass. A separate `alpaca_live` portfolio has its own ledger and never mixes with paper. The DB check constraint is relaxed by a reviewed migration.
  - **Refs:** §17.1, §10.4 · **Depends:** T-132 · **P2**
- [ ] **T-134** Admin kill switch and production KMS
  - **Acceptance:** an admin flag blocks all real submissions instantly (tested). Production KMS key wrapping is in place. The startup guard verifies KMS and alerting before enabling live.
  - **Refs:** §17.9, §18.12 · **Depends:** T-117 · **P2**
- [ ] **T-135** Real-money safety test suite
  - **Acceptance:** order-path proof tests (T-112) extended to `alpaca_live`. Every real order requires its own fresh hold-to-approve. No job can submit a real order without a user-approved assessment.
  - **Refs:** §17.9, A5 · **Depends:** T-133 · **P2**
- [ ] **T-136** Owner sign-off to enable live
  - **Acceptance:** owner reviews T-130–T-135 evidence and enables the flag for their own account only. The doc and ADR are updated.
  - **Refs:** §17.9 · **Depends:** T-130–T-135 · **P2**

---

## Part D — Progress log

| Date | Milestone reached | Commit | Notes |
|---|---|---|---|
| 2026-11-09 | Skeleton verified (pre-M0) | — (uncommitted) | 32/32 .NET tests, stack healthy, app running in simulator (§4.5) |
| 2026-11-10 | T-000 baseline committed | `1b2a253` | Fresh-clone check: 32/32 tests, typecheck green |
| 2026-11-11 | Backend auth & account APIs complete: T-020..T-025 | `2f90540` | 237/237 tests; register/verify, login/refresh/logout, password reset/change, profile & risk settings, data isolation, rate limits |
| 2026-11-10 | Platform plumbing complete: T-014..T-017 | `1f35a80` | 155/155 tests; outbox, job queue, idempotency, audit |
| 2026-11-10 | eToro integration planned (M5b, gated M8) | — | Official eToro API verified; SYSTEM_DESIGN.md v0.3 §17; client flow map in docs/PRODUCT_FLOW.md |
| 2026-11-17 | Brokerage changed to Alpaca (M5b, gated M8 retargeted) | — | Owner decision, ADR-023 supersedes ADR-020; Alpaca Trading API verified; SYSTEM_DESIGN.md v0.4 §17 |
| 2026-11-10 | Platform core: T-004, T-011, T-012, T-013 | `76db047` | 109/109 tests; float ban, money primitives, per-module migrations, RFC 7807 pipeline |
