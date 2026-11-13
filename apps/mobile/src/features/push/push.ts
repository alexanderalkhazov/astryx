import Constants from 'expo-constants';
import * as Device from 'expo-device';
import * as Notifications from 'expo-notifications';
import { type Href, router } from 'expo-router';
import { Platform } from 'react-native';

import { notifications } from '@/lib/api';
import { secureStorage } from '@/lib/secureStorage';

export type PushRegistration = { ok: true } | { ok: false; reason: 'simulator' | 'denied' | 'no_project' | 'failed' };

// While the app is open, Activity and toasts already show new items: no banner on top of them.
Notifications.setNotificationHandler({
  handleNotification: async () => ({ shouldShowBanner: false, shouldShowList: true, shouldPlaySound: false, shouldSetBadge: false }),
});

/**
 * Asks for permission (once), gets this device's Expo push token and registers it (N4). Push needs a physical
 * device and an EAS project id; without either it reports why instead of failing (SYSTEM_DESIGN.md §7.15).
 */
export async function registerForPush(): Promise<PushRegistration> {
  if (!Device.isDevice) return { ok: false, reason: 'simulator' };
  const current = await Notifications.getPermissionsAsync();
  const granted = current.granted || (await Notifications.requestPermissionsAsync()).granted;
  if (!granted) return { ok: false, reason: 'denied' };
  const projectId = (Constants.expoConfig?.extra as { eas?: { projectId?: string } } | undefined)?.eas?.projectId ?? Constants.easConfig?.projectId;
  if (!projectId && process.env.NODE_ENV !== 'test') return { ok: false, reason: 'no_project' };
  try {
    if (Platform.OS === 'android') {
      await Notifications.setNotificationChannelAsync('default', { name: 'Activity', importance: Notifications.AndroidImportance.DEFAULT });
    }
    const token = (await Notifications.getExpoPushTokenAsync(projectId ? { projectId } : {})).data;
    const deviceId = await secureStorage.getDeviceId();
    await notifications.registerDevice({ deviceId, pushToken: token, platform: Platform.OS === 'android' ? 'android' : 'ios' });
    return { ok: true };
  } catch {
    return { ok: false, reason: 'failed' };
  }
}

/** N5, best effort: called on sign-out so a shared phone stops receiving this account's pushes. */
export async function unregisterPush(): Promise<void> {
  try {
    await notifications.unregisterDevice(await secureStorage.getDeviceId());
  } catch {
    // Offline or already signed out: the server revokes dead tokens when Expo reports them.
  }
}

/** Only in-app paths: a push payload never navigates outside the app. */
export function deepLinkFrom(data: unknown): Href | null {
  const link = (data as { deepLink?: unknown } | null)?.deepLink;
  return typeof link === 'string' && link.startsWith('/') && !link.startsWith('//') ? (link as Href) : null;
}

/** Opens the Activity item a tapped push points at (the same route as the item in the feed). */
export function openFromNotification(response: Notifications.NotificationResponse | null): void {
  const link = deepLinkFrom(response?.notification.request.content.data);
  if (link) router.push(link);
}
