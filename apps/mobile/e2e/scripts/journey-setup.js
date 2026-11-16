// Maestro runScript (GraalJS, Maestro's `http` and `json`): a clean, tradeable starting point for journey.yaml.
// Reseeds the fake market, forces a regular session and quotes NVDA at 186.60, above its 186.50 buy stop, so the entry fills at once
// (Development-only /dev/faults), then resets the paper account (P11) so no earlier run's position is in the way.
// journey-quote.js then waits for the quote sync to store that price.
const headers = { 'Content-Type': 'application/json' };
const faults = http.post(`${API_URL}/dev/faults`, {
  headers,
  body: JSON.stringify({ session: 'open', marketData: 'ok', scanner: 'ok', prices: { NVDA: '186.60' } }),
});
if (faults.status !== 200)
  throw new Error(`dev faults: HTTP ${faults.status} (the API must run in Development with Features:DevFaultInjection)`);

// Fixture history shaped for today and a fresh scan, so NVDA's breakout is a live signal whatever day it is.
const reseed = http.post(`${API_URL}/dev/market/reseed`, { headers, body: '{}' });
if (reseed.status !== 200) throw new Error(`reseed: HTTP ${reseed.status}`);

const login = http.post(`${API_URL}/api/v1/auth/login`, {
  headers,
  body: JSON.stringify({ email: EMAIL, password: PASSWORD, deviceId: 'maestro-journey-01' }),
});
if (login.status !== 200) throw new Error(`login: HTTP ${login.status}`);
output.token = json(login.body).accessToken;
const auth = { ...headers, Authorization: `Bearer ${output.token}` };

const reset = http.post(`${API_URL}/api/v1/portfolio/reset`, {
  headers: { ...auth, 'Idempotency-Key': `journey-${Date.now()}` },
  body: JSON.stringify({ confirm: 'RESET' }),
});
if (reset.status !== 200) throw new Error(`reset: HTTP ${reset.status}`);
output.reset = json(reset.body);
