# AiUnavailable (medium)

**What:** the AI provider's circuit breaker is open (`ai_circuit_open`), or more than 50 % of analysis jobs failed in 15 minutes (`ai_jobs_total{outcome="failed"}`).

**Impact:** "Analyze with AI" shows the reliability message. This is research only, so trading, risk and orders are unaffected by design (§12).

**Confirm:**
1. `GET /health/deps` and `GET /api/v1/system/status` (`ai.status`, `ai.model`).
2. Check `ai_jobs_total` by `error_code`: `ai_output_invalid` (the model's JSON fails validation; `qwen3.5:0.8b` often does), `timeout`, or `provider_unavailable`.
3. Make sure Ollama is reachable from the API: `curl http://host.docker.internal:11434/api/tags` from the container.

**Fix:**
- Ollama down: restart it, and check the model is pulled (`ollama list`).
- Invalid output: expected with small models. Track it in `docs/ai-eval/`, and consider a larger model or a newer prompt version.
- Timeouts: increase `Ai:Ollama:TimeoutSeconds`, or reduce concurrency.

**Escalate:** not urgent. Raise it in the next working session.
