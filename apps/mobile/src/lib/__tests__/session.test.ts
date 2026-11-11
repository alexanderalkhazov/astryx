import { queryClient } from '@/lib/queryClient';
import { restoreSession, signIn, signOut } from '@/lib/session';
import { api } from '@/lib/api';
import { useSession } from '@/state/session';
import { useToasts } from '@/state/toasts';

// In-memory stand-in for the iOS Keychain / Android Keystore, recording the accessibility option used.
const mockKeychain = new Map<string, string>();
const mockKeychainOptions: unknown[] = [];
jest.mock('expo-secure-store', () => ({
  WHEN_UNLOCKED_THIS_DEVICE_ONLY: 'WHEN_UNLOCKED_THIS_DEVICE_ONLY',
  getItemAsync: jest.fn(async (key: string) => mockKeychain.get(key) ?? null),
  setItemAsync: jest.fn(async (key: string, value: string, options: unknown) => {
    mockKeychainOptions.push(options);
    mockKeychain.set(key, value);
  }),
  deleteItemAsync: jest.fn(async (key: string) => {
    mockKeychain.delete(key);
  }),
}));

let mockUuid = 0;
jest.mock('expo-crypto', () => ({ randomUUID: () => `00000000-0000-4000-8000-${String(++mockUuid).padStart(12, '0')}` }));

const mockReplace = jest.fn();
jest.mock('expo-router', () => ({ router: { replace: (...args: unknown[]) => mockReplace(...args) } }));

const ME = {
  id: '01a1c9d4-85c3-7a85-beb8-a9ad35024384',
  email: 'trader@example.com',
  emailVerified: true,
  displayName: 'Alex',
  timezone: 'America/New_York',
  locale: 'en-US',
  biometricLock: false,
  tradingMode: 'paper',
  features: { liveTrading: false, strategyConfigure: false, push: false },
};

const tokens = (n: number) => ({
  accessToken: `access-${n}`,
  accessTokenExpiresAt: '2026-11-11T12:15:00Z',
  refreshToken: `refresh-${n}`,
  refreshTokenExpiresAt: '2026-12-11T12:00:00Z',
  tokenType: 'Bearer',
});

const res = (status: number, body?: unknown) => ({ ok: status < 300, status, headers: { get: () => null }, text: async () => (body === undefined ? '' : JSON.stringify(body)) });

type Route = (url: string, init: { headers: Record<string, string>; body?: string }) => ReturnType<typeof res>;
let route: Route;
const fetchMock = jest.fn((url: string, init: { headers: Record<string, string>; body?: string }) => Promise.resolve(route(url, init)));

beforeAll(() => {
  globalThis.fetch = fetchMock as unknown as typeof fetch;
});

beforeEach(() => {
  mockKeychain.clear();
  mockKeychainOptions.length = 0;
  fetchMock.mockClear();
  mockReplace.mockClear();
  useToasts.getState().clear();
  useSession.setState({ status: 'unknown', accessToken: null, accessTokenExpiresAt: null, user: null });
  queryClient.clear();
});

describe('signIn', () => {
  it('keeps the refresh token in the Keychain and the access token only in memory', async () => {
    route = (url) => (url.endsWith('/auth/login') ? res(200, tokens(1)) : res(200, ME));

    await signIn('trader@example.com', 'correct horse battery staple');

    expect(mockKeychain.get('astryx.refreshToken')).toBe('refresh-1');
    expect([...mockKeychain.values()]).not.toContain('access-1');
    expect(mockKeychainOptions.every((o) => (o as { keychainAccessible: string }).keychainAccessible === 'WHEN_UNLOCKED_THIS_DEVICE_ONLY')).toBe(true);
    expect(useSession.getState()).toMatchObject({ status: 'signedIn', accessToken: 'access-1', user: { email: 'trader@example.com' } });
    const loginBody = JSON.parse(fetchMock.mock.calls[0]![1].body!) as { deviceId: string };
    expect(loginBody.deviceId).toBe(mockKeychain.get('astryx.deviceId'));
  });
});

describe('signOut', () => {
  it('revokes the session on the server and clears everything locally', async () => {
    route = (url) => (url.endsWith('/auth/login') ? res(200, tokens(1)) : url.endsWith('/auth/logout') ? res(204) : res(200, ME));
    await signIn('trader@example.com', 'correct horse battery staple');
    queryClient.setQueryData(['portfolio'], { equity: '118181.30' });

    await signOut();

    expect(fetchMock.mock.calls.some(([url]) => url.endsWith('/auth/logout'))).toBe(true);
    expect(mockKeychain.has('astryx.refreshToken')).toBe(false);
    expect(useSession.getState()).toMatchObject({ status: 'signedOut', accessToken: null, user: null });
    expect(queryClient.getQueryData(['portfolio'])).toBeUndefined();
  });

  it('still signs out locally when the server is unreachable', async () => {
    useSession.getState().signIn('access-1', '2026-11-11T12:15:00Z');
    mockKeychain.set('astryx.refreshToken', 'refresh-1');
    fetchMock.mockImplementationOnce(() => Promise.reject(new TypeError('Network request failed')));

    await signOut();

    expect(mockKeychain.has('astryx.refreshToken')).toBe(false);
    expect(useSession.getState().status).toBe('signedOut');
  });
});

describe('session expiry', () => {
  it('shows one "Session expired" toast and routes to sign-in when the refresh is refused', async () => {
    useSession.getState().signIn('stale-access', '2026-11-11T12:15:00Z');
    mockKeychain.set('astryx.refreshToken', 'revoked-refresh');
    mockKeychain.set('astryx.deviceId', 'device-0001');
    route = (url) => (url.endsWith('/auth/refresh') ? res(401, { code: 'session_revoked' }) : res(401, { code: 'token_expired' }));

    const results = await Promise.allSettled([api.request('/api/v1/me'), api.request('/api/v1/me/settings/risk'), api.request('/api/v1/portfolio')]);
    await new Promise((r) => setTimeout(r, 0));

    expect(results.every((r) => r.status === 'rejected')).toBe(true);
    expect(fetchMock.mock.calls.filter(([url]) => url.endsWith('/auth/refresh'))).toHaveLength(1);
    expect(useToasts.getState().toasts.map((t) => t.title)).toEqual(['Session expired']);
    expect(mockReplace).toHaveBeenCalledTimes(1);
    expect(mockReplace).toHaveBeenCalledWith('/sign-in');
    expect(useSession.getState().status).toBe('signedOut');
    expect(mockKeychain.has('astryx.refreshToken')).toBe(false);
  });

  it('renews silently when the refresh succeeds', async () => {
    useSession.getState().signIn('stale-access', '2026-11-11T12:15:00Z');
    mockKeychain.set('astryx.refreshToken', 'refresh-1');
    mockKeychain.set('astryx.deviceId', 'device-0001');
    route = (url, init) =>
      url.endsWith('/auth/refresh') ? res(200, tokens(2)) : init.headers.Authorization === 'Bearer access-2' ? res(200, ME) : res(401, { code: 'token_expired' });

    await api.request('/api/v1/me');

    expect(useSession.getState().accessToken).toBe('access-2');
    expect(mockKeychain.get('astryx.refreshToken')).toBe('refresh-2'); // rotated token persisted
    expect(useToasts.getState().toasts).toHaveLength(0);
    expect(mockReplace).not.toHaveBeenCalled();
  });
});

describe('restoreSession', () => {
  it('signs in from a stored refresh token', async () => {
    mockKeychain.set('astryx.refreshToken', 'refresh-1');
    route = (url) => (url.endsWith('/auth/refresh') ? res(200, tokens(2)) : res(200, ME));

    await restoreSession();

    expect(useSession.getState()).toMatchObject({ status: 'signedIn', accessToken: 'access-2', user: { displayName: 'Alex' } });
  });

  it('deletes a token the server refuses', async () => {
    mockKeychain.set('astryx.refreshToken', 'revoked');
    route = () => res(401, { code: 'session_revoked' });

    await restoreSession();

    expect(useSession.getState().status).toBe('signedOut');
    expect(mockKeychain.has('astryx.refreshToken')).toBe(false);
  });

  it('keeps the token when offline so a later launch can retry', async () => {
    mockKeychain.set('astryx.refreshToken', 'refresh-1');
    fetchMock.mockImplementationOnce(() => Promise.reject(new TypeError('Network request failed')));

    await restoreSession();

    expect(useSession.getState().status).toBe('signedOut');
    expect(mockKeychain.get('astryx.refreshToken')).toBe('refresh-1');
  });

  it('is signed out without a stored token', async () => {
    await restoreSession();

    expect(useSession.getState().status).toBe('signedOut');
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
