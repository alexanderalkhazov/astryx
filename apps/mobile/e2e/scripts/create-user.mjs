// Creates a verified E2E user on the compose stack (API on :5080, Mailpit on :8025) and prints its credentials.
//   node e2e/scripts/create-user.mjs [email] [password]
const api = process.env.API_URL ?? 'http://localhost:5080';
const mailpit = process.env.MAILPIT_URL ?? 'http://localhost:8025';
const email = process.argv[2] ?? `e2e-${Date.now()}@example.com`;
const password = process.argv[3] ?? 'e2e journey passphrase';

const post = (path, body) => fetch(`${api}${path}`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });

const registered = await post('/api/v1/auth/register', { email, password, displayName: 'E2E' });
if (!registered.ok && registered.status !== 409) throw new Error(`register: HTTP ${registered.status}`);

let link;
for (let i = 0; i < 50 && !link; i++) {
  const search = await (await fetch(`${mailpit}/api/v1/search?query=${encodeURIComponent(`to:${email}`)}`)).json();
  const id = search.messages?.[0]?.ID;
  if (id) link = (await (await fetch(`${mailpit}/api/v1/message/${id}`)).json()).Text.match(/userId=([0-9a-f-]{36})&token=([A-Za-z0-9_-]{43})/);
  if (!link) await new Promise((r) => setTimeout(r, 200));
}
if (!link) throw new Error('No verification email arrived in Mailpit.');
const verified = await post('/api/v1/auth/verify-email', { userId: link[1], token: link[2] });
if (!verified.ok) throw new Error(`verify: HTTP ${verified.status}`);
console.log(`EMAIL=${email} PASSWORD='${password}'`);
