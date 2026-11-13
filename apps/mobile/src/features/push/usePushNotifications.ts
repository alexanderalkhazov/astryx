import * as Notifications from 'expo-notifications';
import { useEffect } from 'react';

import { settings } from '@/lib/api';
import { useSession } from '@/state/session';

import { openFromNotification, registerForPush } from './push';

/**
 * Mounted once inside the signed-in app: follows taps on pushes (including the one that cold-started the app) and,
 * when the user has push on, refreshes this device's registration, since Expo tokens can change between launches.
 */
export function usePushNotifications(): void {
  const signedIn = useSession((s) => s.status === 'signedIn');

  useEffect(() => {
    if (!signedIn) return;
    void Notifications.getLastNotificationResponseAsync().then(openFromNotification);
    const subscription = Notifications.addNotificationResponseReceivedListener(openFromNotification);
    void settings
      .notifications()
      .then(({ data }) => (data.pushEnabled ? registerForPush() : undefined))
      .catch(() => undefined); // offline: the next launch tries again
    return () => subscription.remove();
  }, [signedIn]);
}
