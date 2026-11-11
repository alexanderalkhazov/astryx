import { ApiError, createApiClient } from '@astryx/api-client';
import { z } from 'zod';

interface FakeResponse {
  ok: boolean;
  status: number;
  headers: { get: (name: string) => string | null };
  text: () => Promise<string>;
}

const res = (status: number, body?: unknown): FakeResponse => ({
  ok: status >= 200 && status < 300,
  status,
  headers: { get: () => null },
  text: async () => (body === undefined ? '' : JSON.stringify(body)),
});

type Call = { url: string; init: { method?: string; headers: Record<string, string>; body?: string } };

function setup(handler: (call: Call) => FakeResponse | Promise<FakeResponse>, overrides: { refresh?: () => Promise<string> } = {}) {
  const calls: Call[] = [];
  let token: string | null = 'old-token';
  const onSessionExpired = jest.fn();
  const refresh = jest.fn(
    overrides.refresh ??
      (async () => {
        await new Promise((r) => setTimeout(r, 20));
        token = 'new-token';
        return 'new-token';
      }),
  );
  let n = 0;
  const client = createApiClient({
    baseUrl: 'http://api.test',
    getAccessToken: () => token,
    refresh,
    onSessionExpired,
    newCorrelationId: () => `corr-${++n}`,
    fetchImpl: (async (url: string, init: Call['init']) => {
      const call = { url, init };
      calls.push(call);
      return handler(call);
    }) as unknown as typeof fetch,
  });
  return { client, calls, refresh, onSessionExpired };
}

const Me = z.object({ id: z.string() });

describe('createApiClient', () => {
  it('sends auth, correlation, idempotency and If-Match headers', async () => {
    const { client, calls } = setup(() => res(201, { id: 'o1' }));

    await client.request('/api/v1/paper/orders', { method: 'POST', body: { proposalId: 'p1' }, schema: Me, idempotencyKey: 'key-123', ifMatch: '"v1"' });

    const headers = calls[0]!.init.headers;
    expect(calls[0]!.url).toBe('http://api.test/api/v1/paper/orders');
    expect(headers.Authorization).toBe('Bearer old-token');
    expect(headers['X-Correlation-Id']).toBe('corr-1');
    expect(headers['Idempotency-Key']).toBe('key-123');
    expect(headers['If-Match']).toBe('"v1"');
    expect(headers['Content-Type']).toBe('application/json');
    expect(calls[0]!.init.body).toBe('{"proposalId":"p1"}');
  });

  it('omits the token on unauthenticated endpoints', async () => {
    const { client, calls } = setup(() => res(202));

    await client.request('/api/v1/auth/forgot-password', { method: 'POST', body: { email: 'a@b.io' }, auth: false });

    expect(calls[0]!.init.headers.Authorization).toBeUndefined();
  });

  it('turns problem details into a typed ApiError', async () => {
    const { client } = setup(() =>
      res(422, { code: 'validation_failed', title: 'Unprocessable', detail: 'Check the fields.', errors: { email: ['Enter a valid email address.'] }, traceId: 't-1', correlationId: 'corr-1' }),
    );

    const error = await client.request('/api/v1/auth/register', { method: 'POST', body: {}, auth: false }).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 422, code: 'validation_failed', detail: 'Check the fields.', traceId: 't-1', errors: { email: ['Enter a valid email address.'] } });
  });

  it('runs exactly one refresh for concurrent 401s and retries every request with the new token', async () => {
    const { client, calls, refresh } = setup(({ init }) => (init.headers.Authorization === 'Bearer old-token' ? res(401, { code: 'token_expired' }) : res(200, { id: 'me' })));

    const results = await Promise.all([client.request('/a', { schema: Me }), client.request('/b', { schema: Me }), client.request('/c', { schema: Me })]);

    expect(refresh).toHaveBeenCalledTimes(1);
    expect(results.map((r) => r.data.id)).toEqual(['me', 'me', 'me']);
    expect(calls.filter((c) => c.init.headers.Authorization === 'Bearer new-token')).toHaveLength(3);
  });

  it('expires the session when the refresh fails', async () => {
    const { client, refresh, onSessionExpired } = setup(() => res(401, { code: 'token_expired' }), {
      refresh: async () => {
        throw new Error('refresh refused');
      },
    });

    const errors = await Promise.all([client.request('/a').catch((e: unknown) => e), client.request('/b').catch((e: unknown) => e)]);

    expect(refresh).toHaveBeenCalledTimes(1);
    expect(onSessionExpired).toHaveBeenCalled();
    errors.forEach((e) => expect(e).toMatchObject({ status: 401, code: 'session_expired' }));
  });

  it('does not refresh again when the retried request is still unauthorized', async () => {
    const { client, refresh } = setup(() => res(401, { code: 'session_revoked' }));

    const error = await client.request('/a').catch((e: unknown) => e);

    expect(refresh).toHaveBeenCalledTimes(1);
    expect(error).toMatchObject({ status: 401, code: 'session_revoked' });
  });

  it('never refreshes for unauthenticated endpoints', async () => {
    const { client, refresh } = setup(() => res(401, { code: 'invalid_credentials' }));

    const error = await client.request('/api/v1/auth/login', { method: 'POST', body: {}, auth: false }).catch((e: unknown) => e);

    expect(refresh).not.toHaveBeenCalled();
    expect(error).toMatchObject({ code: 'invalid_credentials' });
  });

  it('reports network failures and unexpected response shapes', async () => {
    const offline = setup(() => {
      throw new TypeError('Network request failed');
    });
    const wrongShape = setup(() => res(200, { unexpected: true }));

    await expect(offline.client.request('/a')).rejects.toMatchObject({ status: 0, code: 'network_error' });
    await expect(wrongShape.client.request('/a', { schema: Me })).rejects.toMatchObject({ code: 'invalid_response' });
  });
});
