// Back to the real trading calendar and fixture prices after journey.yaml.
http.post(`${API_URL}/dev/faults`, { headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ session: 'real', prices: {} }) });
