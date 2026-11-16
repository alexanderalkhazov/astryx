# RefreshTokenReuse / LoginFailureSpike (high)

**What:**
- `RefreshTokenReuse`: more than 5 refresh-token reuse detections in an hour (`auth_refresh_reuse_total`). Each one revoked a token family: someone presented a rotated token, or one from another device.
- `LoginFailureSpike`: failed sign-ins above 10× the daily baseline (`auth_login_failures_total`, by reason).

**Impact:** possible token theft or credential stuffing. Rate limits (10/min and 50/h per IP) and the 5-attempt lockout already slow attackers.

**Confirm:**
1. `SELECT action, count(*), min(occurred_at), max(occurred_at) FROM audit.audit_logs WHERE action IN ('auth.refresh_reuse_detected', 'auth.login_failed', 'auth.locked_out') AND occurred_at > now() - interval '1 hour' GROUP BY action;`
2. Group failures by `metadata->>'reason'` (`unknown_email` points to stuffing; `wrong_password` on few accounts points to targeted guessing).
3. Check the client IPs at the edge (load balancer logs). The API logs no credentials or emails.

**Fix:**
- Stuffing from a few IPs: block them at the edge (WAF).
- One user's tokens reused: their family is already revoked. If needed, force "sign out of all devices" for that user (security-stamp rotation) and tell them.
- Lower `RateLimits:AuthPerMinute` temporarily if the pressure continues.

**Escalate:** if any reuse involves accounts with eToro connections (from T-141), or the spike lasts over an hour.
