import type { CurrentUser } from '@astryx/api-client';
import { create } from 'zustand';

export type SessionStatus = 'unknown' | 'signedOut' | 'signedIn';

interface SessionState {
  /** `unknown` until startup has tried to restore a stored session. */
  status: SessionStatus;
  /** Access token in memory only; never persisted (SYSTEM_DESIGN.md §8.9). */
  accessToken: string | null;
  accessTokenExpiresAt: string | null;
  user: CurrentUser | null;
  /** True after an explicit sign-out, so the auth gate does not stash the screen the user left (T-034). */
  signedOutByUser: boolean;
  signIn: (accessToken: string, expiresAt: string) => void;
  setUser: (user: CurrentUser) => void;
  signOut: (byUser?: boolean) => void;
}

export const useSession = create<SessionState>((set) => ({
  status: 'unknown',
  accessToken: null,
  accessTokenExpiresAt: null,
  user: null,
  signedOutByUser: false,
  signIn: (accessToken, expiresAt) => set({ status: 'signedIn', accessToken, accessTokenExpiresAt: expiresAt, signedOutByUser: false }),
  setUser: (user) => set({ user }),
  signOut: (byUser = false) =>
    set({ status: 'signedOut', accessToken: null, accessTokenExpiresAt: null, user: null, signedOutByUser: byUser }),
}));
