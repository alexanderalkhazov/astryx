// Load smoke test (SYSTEM_DESIGN.md §2.5, §21.2, TASKS.md T-116) against the compose stack with fake data.
// 50 VUs, each its own paper account: one NVDA proposal, five risk assessments, then the same order sent twice at
// once with one Idempotency-Key. 50 more VUs read the portfolio and signals for a minute. Thresholds: reads and
// assessments p95 ≤ 300 ms (NFR-01), orders p95 ≤ 500 ms (NFR-02). Teardown proves zero duplicate fills: every
// account holds exactly one 64-share position and spent exactly one fill's cash.
//
//   docker run --rm -i --network astryx_default --add-host host.docker.internal:host-gateway \
//     -e API_URL=http://host.docker.internal:5080 -v "$PWD/tests/load:/load" grafana/k6:2.3.0 run /load/smoke.js
// (API calls go through the published port, so the per-IP auth limit counts them like any local client.)
//
// Development-only routes prepare the market (POST /dev/market/reseed, /dev/faults), as for the E2E flows. The 50
// accounts are created once by `node tests/load/create-users.mjs`; each run signs them in and resets them (P11).
import { check, sleep } from 'k6';
import http from 'k6/http';
import exec from 'k6/execution';

const API = __ENV.API_URL || 'http://api:8080';
const USERS = 50;
const PASSWORD = 'load smoke passphrase';
const JSON_HEADERS = { 'Content-Type': 'application/json' };

export const options = {
  setupTimeout: '10m', // 50 sign-ins under the local 60/min per-IP limit
  scenarios: {
    trade: { executor: 'per-vu-iterations', vus: USERS, iterations: 1, maxDuration: '5m', exec: 'trade' },
    reads: { executor: 'constant-vus', vus: USERS, duration: '60s', exec: 'reads', startTime: '5s' },
  },
  thresholds: {
    'http_req_duration{kind:read}': ['p(95)<300'],
    'http_req_duration{kind:assess}': ['p(95)<300'],
    'http_req_duration{kind:order}': ['p(95)<500'],
    'checks{phase:trade}': ['rate==1'],
    'checks{phase:verify}': ['rate==1'],
  },
};

const json = (r) => {
  try {
    return r.json();
  } catch {
    return null;
  }
};

function post(path, body, token, extra = {}) {
  const headers = { ...JSON_HEADERS, ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(extra.headers || {}) };
  return http.post(`${API}${path}`, JSON.stringify(body), { headers, tags: extra.tags });
}

/** An auth call that waits out the per-IP limit (429) instead of failing setup. */
function authPost(path, body) {
  for (let attempt = 0; attempt < 30; attempt++) {
    const r = post(path, body);
    if (r.status !== 429) return r;
    sleep(Number(r.headers['Retry-After']) || 5);
  }
  throw new Error(`${path}: still rate limited`);
}

/** Sign in an account made by create-users.mjs and start it from a clean paper account (P11). */
function prepareUser(index) {
  const email = `load-user-${index}@example.com`;
  const login = authPost('/api/v1/auth/login', { email, password: PASSWORD, deviceId: `k6-load-device-${index}` });
  if (login.status !== 200) throw new Error(`login ${email}: ${login.status} (run tests/load/create-users.mjs first)`);
  sleep(1.1); // stay under 60 auth calls a minute
  const token = json(login).accessToken;
  const reset = post('/api/v1/portfolio/reset', { confirm: 'RESET' }, token, { headers: { 'Idempotency-Key': `k6-reset-${index}-${Date.now()}` } });
  if (reset.status !== 200) throw new Error(`reset ${email}: ${reset.status}`);
  return token;
}

export function setup() {
  const faults = post('/dev/faults', { session: 'open', marketData: 'ok', scanner: 'ok', prices: { NVDA: '186.60' } });
  if (faults.status !== 200) throw new Error(`dev faults: ${faults.status} (needs Development with Features:DevFaultInjection)`);
  if (post('/dev/market/reseed', {}).status !== 200) throw new Error('reseed failed');
  // Quote sync stores the pinned price within a minute; orders need it (and a fresh quote) to fill at once.
  for (let i = 0; i < 40; i++) {
    const status = json(http.get(`${API}/api/v1/system/status`)).marketData;
    if (status.quotesAsOf && Date.now() - Date.parse(status.quotesAsOf) < 30000 && !status.quotesStale) break;
    sleep(3);
  }

  const tokens = [];
  for (let i = 0; i < USERS; i++) tokens.push(prepareUser(i));
  return { tokens };
}

export function trade(data) {
  const token = data.tokens[exec.scenario.iterationInTest]; // VU ids span both scenarios; iterations are 0…49 here
  const auth = (tags) => ({ headers: { Authorization: `Bearer ${token}` }, tags });
  const signalsResponse = http.get(`${API}/api/v1/signals?symbol=NVDA`, auth({ kind: 'read', name: 'signals' }));
  const signals = json(signalsResponse);
  const signalId = signals && signals.items && signals.items[0] && signals.items[0].id;
  if (!signalId) console.warn(`VU ${exec.vu.idInTest}: signals ${signalsResponse.status} ${signalsResponse.body && signalsResponse.body.slice(0, 120)}`);
  check(signalId, { 'NVDA signal': (id) => !!id }, { phase: 'trade' });

  const proposal = json(post('/api/v1/proposals', { source: 'signal', signalId }, token, { tags: { kind: 'proposal' } }));
  check(proposal, { 'proposal created': (p) => !!(p && p.id) }, { phase: 'trade' });

  let assessment = proposal.assessment;
  for (let i = 0; i < 5; i++) {
    const r = post(`/api/v1/proposals/${proposal.id}/assessments`, { riskPerTradePct: '0.0075' }, token, { tags: { kind: 'assess' } });
    check(r, { 'assessment 201': (x) => x.status === 201 }, { phase: 'trade' });
    assessment = json(r) || assessment;
  }

  // The same intent twice at once: one order, one fill.
  const key = `k6-${exec.vu.idInTest}-${Date.now()}`;
  const body = JSON.stringify({ proposalId: proposal.id, assessmentId: assessment.id });
  const params = { headers: { ...JSON_HEADERS, Authorization: `Bearer ${token}`, 'Idempotency-Key': key }, tags: { kind: 'order' } };
  const [a, b] = http.batch([
    ['POST', `${API}/api/v1/paper/orders`, body, params],
    ['POST', `${API}/api/v1/paper/orders`, body, params],
  ]);
  check([a, b], { 'order accepted (replay or in progress allowed)': (rs) => rs.some((r) => r.status === 201) && rs.every((r) => [200, 201, 409].includes(r.status)) }, { phase: 'trade' });
}

export function reads(data) {
  const token = data.tokens[(exec.vu.idInTest - 1) % data.tokens.length];
  const params = (name) => ({ headers: { Authorization: `Bearer ${token}` }, tags: { kind: 'read', name } });
  http.batch([
    ['GET', `${API}/api/v1/portfolio`, null, params('portfolio')],
    ['GET', `${API}/api/v1/signals`, null, params('signals')],
    ['GET', `${API}/api/v1/portfolio/positions?status=open`, null, params('positions')],
  ]);
  sleep(2); // ~90 requests a minute per account, under the 120/min per-user limit the traders share

}

export function teardown(data) {
  for (const token of data.tokens) {
    const params = { headers: { Authorization: `Bearer ${token}` } };
    const positions = json(http.get(`${API}/api/v1/portfolio/positions?status=open`, params));
    const summary = json(http.get(`${API}/api/v1/portfolio`, params));
    check(positions, { 'exactly one position': (p) => !!(p && p.items && p.items.length === 1 && p.items[0].quantity === '64') }, { phase: 'verify' });
    // 100,000 − 64 × 186.62 (one fill at the 186.50 stop plus a cent of slippage).
    check(summary, { 'one fill of cash spent': (s) => s && s.cash === '88056.32' }, { phase: 'verify' });
  }
  post('/dev/faults', { session: 'real', prices: {} });
}
