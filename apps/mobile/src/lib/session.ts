import { isApiError } from '@astryx/api-client';

import { auth, clearLocalSession, refreshSession } from '@/lib/api';
import { secureStorage } from '@/lib/secureStorage';
import { useSession } from '@/state/session';

/** Signs in and keeps the refresh token in secure storage; the access token stays in memory. */
export async function signIn(email: string, password: string): Promise<void> {
  const deviceId = await secureStorage.getDeviceId();
  const { data } = await auth.login({ email, password, deviceId });
  await secureStorage.setRefreshToken(data.refreshToken);
  useSession.getState().signIn(data.accessToken, data.accessTokenExpiresAt);
  const me = await auth.me();
  useSession.getState().setUser(me.data);
}

/** Ends the session on the server (best effort) and clears everything locally either way. */
export async function signOut(): Promise<void> {
  const refreshToken = await secureStorage.getRefreshToken();
  try {
    if (refreshToken && useSession.getState().accessToken) {
      await auth.logout({ refreshToken });
    }
  } catch {
    // Offline or already revoked: local sign-out still happens.
  } finally {
    await clearLocalSession();
  }
}

/**
 * At app start: restore a session from the stored refresh token. Network failures keep the token for a later retry
 * but leave the app signed out; a refused token is deleted.
 */
export async function restoreSession(): Promise<void> {
  if (!(await secureStorage.getRefreshToken())) {
    useSession.getState().signOut();
    return;
  }

  try {
    await refreshSession();
    const me = await auth.me();
    useSession.getState().setUser(me.data);
  } catch (error) {
    if (isApiError(error) && error.isNetworkError) {
      useSession.getState().signOut();
      return;
    }

    await clearLocalSession();
  }
}
