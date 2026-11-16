import {
  analysesApi,
  authApi,
  notificationsApi,
  paperApi,
  portfolioApi,
  proposalsApi,
  createApiClient,
  marketApi,
  scannerApi,
  searchApi,
  settingsApi,
  strategiesApi,
  watchlistsApi,
} from '@astryx/api-client';
import { randomUUID } from 'expo-crypto';
import { router } from 'expo-router';

import { apiUrl } from '@/lib/config';
import { queryClient } from '@/lib/queryClient';
import { secureStorage } from '@/lib/secureStorage';
import { useSession } from '@/state/session';
import { toast } from '@/state/toasts';

/** Renews the session from the stored refresh token. Throws when there is none or the server refuses it. */
async function refreshSession(): Promise<string> {
  const [refreshToken, deviceId] = await Promise.all([secureStorage.getRefreshToken(), secureStorage.getDeviceId()]);
  if (!refreshToken) throw new Error('No stored session');
  const { data } = await auth.refresh({ refreshToken, deviceId });
  await secureStorage.setRefreshToken(data.refreshToken);
  useSession.getState().signIn(data.accessToken, data.accessTokenExpiresAt);
  return data.accessToken;
}

/** Clears everything a session left behind: memory, Keychain token and cached server data. */
export async function clearLocalSession(byUser = false): Promise<void> {
  useSession.getState().signOut(byUser);
  queryClient.clear();
  await secureStorage.clearRefreshToken();
}

/** Refresh failed mid-session: tell the user once and send them to sign in. */
function handleSessionExpired(): void {
  if (useSession.getState().status !== 'signedIn') return; // already handled by a concurrent request
  void clearLocalSession();
  toast({ tone: 'warning', title: 'Session expired', body: 'Sign in again to continue.' });
  router.replace('/sign-in');
}

export const api = createApiClient({
  baseUrl: apiUrl,
  getAccessToken: () => useSession.getState().accessToken,
  refresh: refreshSession,
  onSessionExpired: handleSessionExpired,
  newCorrelationId: randomUUID,
});

export const auth = authApi(api);
export const market = marketApi(api);
export const search = searchApi(api);
export const watchlists = watchlistsApi(api);
export const scanner = scannerApi(api);
export const analyses = analysesApi(api);
export const proposals = proposalsApi(api);
export const paper = paperApi(api);
export const portfolio = portfolioApi(api);
export const notifications = notificationsApi(api);

export { refreshSession };
export const strategies = strategiesApi(api);
export const settings = settingsApi(api);
