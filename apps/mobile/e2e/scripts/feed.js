// Market data freshness from /system/status: output.feedOk (live, fresh quotes) and output.quotesOld (older than the
// risk engine's 90 s limit, so approvals are disabled).
const status = json(http.get(`${API_URL}/api/v1/system/status`).body).marketData;
const age = status.quotesAsOf ? Date.now() - Date.parse(status.quotesAsOf) : Infinity;
output.feedOk = !status.feedDown && !status.quotesStale && age < 60000;
output.quotesOld = age > 95000;
