# LiveTradingEnabled (critical)

**What:** `live_trading_enabled` is 1. `Features:LiveTrading` is true in an environment. In the MVP this must never happen (§17.9), and the API's startup guard should already have refused to start.

**Impact:** possible real-money execution paths. The guard and a 403 on every live request remain, but treat this as a configuration breach.

**Confirm:**
1. Check the environment's configuration: `Features__LiveTrading` (env), app settings and secret-manager values.
2. Check the API logs for the startup guard message "Features:LiveTrading is enabled…". If the API is running anyway, the guard was bypassed: a severe bug.

**Fix:**
1. Set `Features:LiveTrading=false` and redeploy.
2. Check `audit.audit_logs` for `trading_mode.live_attempt` rows during the window.
3. Confirm no `alpaca_live` portfolio exists: `SELECT count(*) FROM portfolio.portfolios WHERE mode = 'alpaca_live';` must be 0 (the check constraint forbids it).

**Escalate:** immediately, to the owner. File an incident even if nothing executed.
