# OWASP ASVS 4.0.3 Level 1 review

Scope: the API (`apps/api` plus the modules) and the mobile app, as of 2026-11-16 (T-115). Status is **Met**, **Gap**
(with the decision it needs), or **N/A**. Evidence points at code or tests. Re-run this review before T-119, the MVP
release gate.

## Summary

| Area | Status |
|---|---|
| V2 Authentication | Met, except 2.1.7: breached-password check (Gap) |
| V3 Session management | Met |
| V4 Access control | Met |
| V5 Validation, sanitisation and encoding | Met |
| V6 Stored cryptography | Met (eToro key envelope encryption lands with T-141) |
| V7 Error handling and logging | Met |
| V8 Data protection | Met |
| V9 Communications | Met at the edge (TLS terminates at the load balancer, T-117) |
| V10 Malicious code | Met |
| V11 Business logic | Met |
| V12 Files and resources | N/A (no uploads) |
| V13 API and web service | Met |
| V14 Configuration | Met |

## V2 Authentication

| # | Requirement | Status | Evidence |
|---|---|---|---|
| 2.1.1 | Passwords of at least 12 characters | Met | `IdentityEndpoints` validators: `Length(12, 128)` on register, reset and change |
| 2.1.2 | Passwords of 64 characters or more allowed | Met | Up to 128 characters |
| 2.1.3 | No truncation | Met | ASP.NET Core Identity hasher over the full string |
| 2.1.4 | Any printable Unicode, including spaces and emoji | Met | No character-class rules |
| 2.1.5–2.1.6 | Users can change their password (with the current one) | Met | `POST /me/change-password` needs the current password; Settings › Change password |
| 2.1.7 | Passwords checked against a breached-password list | **Gap** | Needs a decision: the Have I Been Pwned k-anonymity range API (an outbound call per password set) or an offline top-N list. Suggested before T-119 |
| 2.1.8 | Password strength meter | N/A at L1 (recommended) | — |
| 2.1.9 | No composition rules | Met | Length only |
| 2.1.10 | No periodic rotation | Met | — |
| 2.1.11 | Pasting and password managers allowed | Met | Inputs set `textContentType` and `autoComplete` |
| 2.1.12 | Show/hide password | Met | `Input` secure entry; iOS offers reveal through the password manager |
| 2.2.1 | Anti-automation | Met | `auth-strict` rate limits (10/min, 50/h per IP); lockout after 5 failures for 15 min (`SessionService`) |
| 2.2.2 | No weak authenticators (SMS) | Met | Password plus device-bound refresh token only |
| 2.2.3 | Notification after sensitive changes | Met | Password change and reset emails; logout-all through the security stamp |
| 2.3.1 | Initial secrets random and expiring | Met | Email verification and reset tokens: 32 random bytes, single use, expiring (§18.1, §18.4) |
| 2.4.1–2.4.2 | Salted adaptive hashing | Met | PBKDF2-SHA512, 600,000 iterations (`AccountOptions.PasswordHashIterations`) |
| 2.5.1–2.5.4 | Recovery does not reveal accounts; no hints or questions | Met | `forgot-password` always answers 202 |
| 2.5.6 | Recovery uses a secure out-of-band token | Met | Emailed link token, hashed at rest |
| 2.7–2.8 | Out-of-band and one-time verifiers | N/A | Not offered (MFA is Phase 8, before real money) |

## V3 Session management

| # | Requirement | Status | Evidence |
|---|---|---|---|
| 3.1.1 | No session tokens in URLs | Met, with one exception | SignalR must send `?access_token=`. It is short-lived, and `Microsoft.AspNetCore` logs at Warning, so query strings are never logged (`SensitiveLoggingTests`); OTel redacts query strings |
| 3.2.1 | New token on authentication | Met | New refresh family per login |
| 3.2.2–3.2.3 | Tokens of at least 64 bits of entropy, stored securely on the device | Met | 256-bit refresh tokens, hashed at rest; iOS Keychain or Android Keystore through SecureStore; the access token stays in memory |
| 3.3.1 | Logout invalidates the session | Met | Logout revokes the family; logout-all also rotates the security stamp (`SessionTests`) |
| 3.3.2 | Re-authentication after a period | Met | 15-minute access tokens; refresh families capped at 90 days |
| 3.4 | Cookie attributes | N/A | No cookies |
| 3.5.2–3.5.3 | Stateless tokens are signed and validated | Met | ES256 JWT with `kid`, issuer, audience, lifetime and security-stamp checks (`AuthenticationSetup`); rotation drill in `JwtKeyRotationTests` |
| 3.7.1 | Re-authentication before sensitive actions | Met for MVP | Password change needs the current password. Live trading is blocked server-side; its re-auth is part of the live-gate work (§17.9) |

## V4 Access control

| # | Requirement | Status | Evidence |
|---|---|---|---|
| 4.1.1–4.1.3 | Enforced server-side, deny by default | Met | `RequireAuthorization("Authenticated")` on every module's route group. Anonymous endpoints: `auth/*` (register, verify, login, refresh, reset), `/health/*` and `/system/status`. `DataIsolationTests` enumerates routes. Suggestion: add an ASP.NET Core fallback policy so a forgotten group defaults to authenticated |
| 4.1.5 | Fails securely | Met | Unknown or foreign resource → 404 (`DataIsolationTests` covers every parameterised route) |
| 4.2.1 | IDOR protection | Met | `IUserOwned` global query filters (`UserOwnershipTests`), per-route isolation fixtures |
| 4.2.2 | CSRF | N/A | Bearer tokens, no cookies |
| 4.3.1 | Admin interfaces | N/A | None; `/dev/*` exists only in Development with `Features:DevFaultInjection` (`DevFaultsAbsentTests`) |

## V5 Validation, sanitisation and encoding

| # | Requirement | Status | Evidence |
|---|---|---|---|
| 5.1.1–5.1.4 | Input validated against an allow list, typed and range-checked | Met | FluentValidation on every write (422); decimals as strings with scale limits; enums checked |
| 5.1.5 | Redirects | N/A | None; push deep links follow in-app paths only (`deepLinkFrom`) |
| 5.2.x | Sanitisation of untrusted content | Met | News passes through `NewsSanitizer` before reaching AI prompts; AI output is schema-validated and never executed (§12.7) |
| 5.3.4 | Parameterised queries | Met | EF Core LINQ and interpolated `ExecuteSqlAsync`/`SqlQuery` (parameterised); no string-built SQL with user input |
| 5.3.3 | Output encoding | Met | JSON serialisation only; the app renders text, not HTML |
| 5.5.x | Safe deserialisation | Met | System.Text.Json into typed DTOs; outbox and job payloads are typed |

## V6 Stored cryptography

| # | Requirement | Status | Evidence |
|---|---|---|---|
| 6.2.1 | Modules fail securely | Met | Malformed signing keys refuse to start (`JwtKeyRotationTests`) |
| 6.2.2 | Proven algorithms | Met | ES256, PBKDF2-SHA512, SHA-256 token hashes, AES-GCM envelope design for broker keys (§18.12, T-141) |
| 6.4.1–6.4.2 | Key management and rotation | Met | Signing keys come from the environment or secret manager; two-key overlap rotation, drilled in `JwtKeyRotationTests` |

## V7 Error handling and logging

| # | Requirement | Status | Evidence |
|---|---|---|---|
| 7.1.1–7.1.2 | No credentials, session tokens or personal data in logs | Met | No log template names a password, token, key or email (`SensitiveLoggingTests`); EF sensitive-data logging never enabled; request bodies not logged |
| 7.1.3–7.1.4 | Security events logged with context | Met | Audit log for auth, settings, orders, resets, live attempts and reconciliation; correlation ids on every log line |
| 7.4.1 | Generic errors with a reference id | Met | RFC 7807 problems with `correlationId`; no stack traces outside Development |

## V8 Data protection

| # | Requirement | Status | Evidence |
|---|---|---|---|
| 8.1.1 | No sensitive data in caches | Met | `Cache-Control: no-store` on `/api` (`PipelineTests`) |
| 8.2.1–8.2.3 | Client storage | Met | Only the refresh token and device id are persisted (SecureStore); the query cache lives in memory; balances are blurred in the iOS app switcher and protected by FLAG_SECURE on Android (`useScreenProtection`) |
| 8.3.1 | Sensitive data in bodies or headers, not URLs | Met | Except the SignalR token (see 3.1.1) |
| 8.3.4 | Personal data inventory | Met | Email, display name, paper portfolio. Sentry runs without PII and strips query strings |

## V9 Communications

| # | Requirement | Status | Evidence |
|---|---|---|---|
| 9.1.1–9.1.3 | TLS for all connections, strong ciphers | Met at the edge | Staging and production terminate TLS 1.2+ at the load balancer (T-117); local dev is HTTP on localhost. App Transport Security stays on in release builds |

## V10–V14

| # | Requirement | Status | Evidence |
|---|---|---|---|
| 10.3.2 | No untrusted dependency scripts | Met | pnpm `allowBuilds` gates install scripts (`@parcel/watcher` and `@sentry/cli` deliberately not run); pinned versions at least two weeks old |
| 11.1.1–11.1.5 | Business logic in order, with limits and anti-automation | Met | Proposal → assessment → hold-to-confirm → gate re-run; per-user order and assessment rate limits; idempotency keys (`OrderPathSafetyTests`, k6 duplicate-fill check in T-116) |
| 12.x | File uploads | N/A | — |
| 13.1.3 | API URLs carry no secrets | Met | — |
| 13.2.1 | HTTP methods enforced | Met | Minimal API route verbs |
| 13.2.3 | CSRF for cookie auth | N/A | — |
| 13.2.5 | Content-Type checked | Met | JSON body binding refuses other types (415) |
| 14.2.1 | Dependencies up to date | Met | CI `security` job: vulnerable NuGet (any severity) and npm production dependencies (high and critical) fail; weekly Dependabot PRs with a 14-day cooldown (T-002) |
| 14.3.2 | Debug features off in production | Met | `/dev/*` and the OpenAPI document are Development-only; the live-trading startup guard |
| 14.4.1–14.4.7 | Security headers | Met | `nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, a restrictive CSP (`HttpPipeline.SecurityHeaders`, `PipelineTests`) |
| 14.5.3 | CORS allow list | Met | No CORS policy: native client only |

## Open items

1. **2.1.7 breached passwords.** Choose HIBP k-anonymity or an offline list (owner decision, before T-119).
2. **Postgres row-level security.** Evaluated and deferred (ADR 022, `docs/adr/022-postgres-row-level-security.md`).
