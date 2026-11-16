// Creates the load test's verified accounts once (load-user-0…49@example.com) on the compose stack, through the
// public API and Mailpit, paced under the per-IP auth limit. Existing accounts are skipped, so it can be rerun.
//   node tests/load/create-users.mjs
const api = process.env.API_URL ?? 'http://localhost:5080';
const mailpit = process.env.MAILPIT_URL ?? 'http://localhost:8025';
const count = Number(process.env.USERS ?? 50);
export const PASSWORD = 'load smoke passphrase';

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function authPost(path, body) {
  for (;;) {
    const r = await fetch(`${api}${path}`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    if (r.status !== 429) {
      await sleep(1100); // stay under 60 auth calls a minute
      return r;
    }
    await sleep((Number(r.headers.get('retry-after')) || 5) * 1000);
  }
}

for (let i = 0; i < count; i++) {
  const email = `load-user-${i}@example.com`;
  const login = await authPost('/api/v1/auth/login', { email, password: PASSWORD, deviceId: 'load-user-check' });
  if (login.status === 200) {
    console.log(`${email}: exists`);
    continue;
  }

  const registered = await authPost('/api/v1/auth/register', { email, password: PASSWORD, displayName: 'Load' });
  if (registered.status >= 300 && registered.status !== 409) throw new Error(`register ${email}: HTTP ${registered.status}`);
  let link;
  for (let attempt = 0; attempt < 50 && !link; attempt++) {
    const search = await (await fetch(`${mailpit}/api/v1/search?query=${encodeURIComponent(`to:${email}`)}`)).json();
    const id = search.messages?.[0]?.ID;
    if (id)
      link = (await (await fetch(`${mailpit}/api/v1/message/${id}`)).json()).Text.match(/userId=([0-9a-f-]{36})&token=([A-Za-z0-9_-]{43})/);
    if (!link) await sleep(200);
  }
  if (!link) throw new Error(`no verification email for ${email}`);
  const verified = await authPost('/api/v1/auth/verify-email', { userId: link[1], token: link[2] });
  if (verified.status >= 300) throw new Error(`verify ${email}: HTTP ${verified.status}`);
  console.log(`${email}: created`);
}
