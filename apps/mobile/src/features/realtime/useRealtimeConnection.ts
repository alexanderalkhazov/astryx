import { useEffect } from 'react';

import { refreshSession } from '@/lib/api';
import { apiUrl } from '@/lib/config';
import { queryClient } from '@/lib/queryClient';
import { useSession } from '@/state/session';

import { startRealtime } from './realtime';

/** Refresh this long before expiry, so a reconnect never presents a token that dies mid-negotiate. */
const EXPIRY_MARGIN_MS = 30_000;

async function freshAccessToken(): Promise<string> {
  const { accessToken, accessTokenExpiresAt } = useSession.getState();
  if (accessToken && accessTokenExpiresAt && Date.parse(accessTokenExpiresAt) - Date.now() > EXPIRY_MARGIN_MS) return accessToken;
  return refreshSession();
}

/** Keeps the hub connected for the signed-in session; mounted once by the (app) layout. */
export function useRealtimeConnection(): void {
  // A different user always passes through signed-out, which tears the connection down.
  const signedIn = useSession((s) => s.status === 'signedIn');

  useEffect(() => {
    if (!signedIn) return;
    return startRealtime({ baseUrl: apiUrl, getAccessToken: freshAccessToken, queryClient });
  }, [signedIn]);
}
