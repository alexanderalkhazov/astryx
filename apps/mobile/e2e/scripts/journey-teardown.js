// Back to the real trading calendar, fixture prices, a healthy feed and scanner after journey.yaml or states.yaml.
http.post(`${API_URL}/dev/faults`, {
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ session: 'real', prices: {}, marketData: 'ok', scanner: 'ok' }),
});
