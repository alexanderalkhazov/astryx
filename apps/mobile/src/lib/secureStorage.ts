import { randomUUID } from 'expo-crypto';
import * as SecureStore from 'expo-secure-store';

// Only the refresh token, the device id, which accounts accepted the onboarding disclaimer, each account's
// Simple/Pro view and its onboarding answers are persisted, in the iOS
// Keychain / Android Keystore, readable only while the device is unlocked and never migrated to another device
// (SYSTEM_DESIGN.md §8.9, §18.6).
const OPTIONS: SecureStore.SecureStoreOptions = { keychainAccessible: SecureStore.WHEN_UNLOCKED_THIS_DEVICE_ONLY };

const REFRESH_TOKEN = 'astryx.refreshToken';
const DEVICE_ID = 'astryx.deviceId';
/** Bumped when the disclaimer text changes materially, so everyone sees it again (T-118). */
const DISCLAIMER = (userId: string) => `astryx.disclaimer.v1.${userId}`;
const VIEW_MODE = (userId: string) => `astryx.viewMode.${userId}`;
const PROFILE = (userId: string) => `astryx.profile.v1.${userId}`;
const GOAL = (userId: string) => `astryx.goal.v1.${userId}`;
const GUIDED_TRADE = (userId: string) => `astryx.guidedTrade.v1.${userId}`;
const STARTED = (userId: string) => `astryx.started.v1.${userId}`;

export const secureStorage = {
  getRefreshToken: () => SecureStore.getItemAsync(REFRESH_TOKEN, OPTIONS),
  setRefreshToken: (token: string) => SecureStore.setItemAsync(REFRESH_TOKEN, token, OPTIONS),
  clearRefreshToken: () => SecureStore.deleteItemAsync(REFRESH_TOKEN, OPTIONS),

  hasAcceptedDisclaimer: async (userId: string) => (await SecureStore.getItemAsync(DISCLAIMER(userId), OPTIONS)) === 'accepted',
  acceptDisclaimer: (userId: string) => SecureStore.setItemAsync(DISCLAIMER(userId), 'accepted', OPTIONS),
  getViewMode: (userId: string) => SecureStore.getItemAsync(VIEW_MODE(userId), OPTIONS),
  setViewMode: (userId: string, mode: 'simple' | 'pro') => SecureStore.setItemAsync(VIEW_MODE(userId), mode, OPTIONS),
  getProfile: (userId: string) => SecureStore.getItemAsync(PROFILE(userId), OPTIONS),
  setProfile: (userId: string, json: string) => SecureStore.setItemAsync(PROFILE(userId), json, OPTIONS),
  getGoal: (userId: string) => SecureStore.getItemAsync(GOAL(userId), OPTIONS),
  setGoal: (userId: string, goal: string) => SecureStore.setItemAsync(GOAL(userId), goal, OPTIONS),
  getGuidedTrade: (userId: string) => SecureStore.getItemAsync(GUIDED_TRADE(userId), OPTIONS),
  setGuidedTrade: (userId: string, json: string) => SecureStore.setItemAsync(GUIDED_TRADE(userId), json, OPTIONS),
  getStarted: (userId: string) => SecureStore.getItemAsync(STARTED(userId), OPTIONS),
  setStarted: (userId: string, json: string) => SecureStore.setItemAsync(STARTED(userId), json, OPTIONS),

  /** A random id generated on first use; refresh-token families are bound to it on the server. */
  async getDeviceId(): Promise<string> {
    const existing = await SecureStore.getItemAsync(DEVICE_ID, OPTIONS);
    if (existing) return existing;
    const created = randomUUID();
    await SecureStore.setItemAsync(DEVICE_ID, created, OPTIONS);
    return created;
  },
};
