import { randomUUID } from 'expo-crypto';
import * as SecureStore from 'expo-secure-store';

// Only the refresh token and the device id are persisted, in the iOS Keychain / Android Keystore, readable only while
// the device is unlocked and never migrated to another device (SYSTEM_DESIGN.md §8.9, §18.6).
const OPTIONS: SecureStore.SecureStoreOptions = { keychainAccessible: SecureStore.WHEN_UNLOCKED_THIS_DEVICE_ONLY };

const REFRESH_TOKEN = 'astryx.refreshToken';
const DEVICE_ID = 'astryx.deviceId';

export const secureStorage = {
  getRefreshToken: () => SecureStore.getItemAsync(REFRESH_TOKEN, OPTIONS),
  setRefreshToken: (token: string) => SecureStore.setItemAsync(REFRESH_TOKEN, token, OPTIONS),
  clearRefreshToken: () => SecureStore.deleteItemAsync(REFRESH_TOKEN, OPTIONS),

  /** A random id generated on first use; refresh-token families are bound to it on the server. */
  async getDeviceId(): Promise<string> {
    const existing = await SecureStore.getItemAsync(DEVICE_ID, OPTIONS);
    if (existing) return existing;
    const created = randomUUID();
    await SecureStore.setItemAsync(DEVICE_ID, created, OPTIONS);
    return created;
  },
};
