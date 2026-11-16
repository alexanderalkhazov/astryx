// Is the stored NVDA quote at the E2E price yet? (Quote sync runs every minute.) Sets output.quoteReady.
const quote = http.get(`${API_URL}/api/v1/market/NVDA/quote`, { headers: { Authorization: `Bearer ${output.token}` } });
output.quoteReady = quote.status === 200 && String(json(quote.body).last).indexOf('186.6') === 0;
