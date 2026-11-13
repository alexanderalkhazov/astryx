import * as Device from 'expo-device';
import * as Notifications from 'expo-notifications';
import { router } from 'expo-router';

import { deepLinkFrom, openFromNotification, registerForPush, unregisterPush } from '../push';

jest.mock('expo-router', () => ({ router: { push: jest.fn() } }));
jest.mock('@/lib/secureStorage', () => ({ secureStorage: { getDeviceId: jest.fn(() => Promise.resolve('device-0001')) } }));
jest.mock('@/lib/api', () => ({ notifications: { registerDevice: jest.fn(() => Promise.resolve({})), unregisterDevice: jest.fn(() => Promise.resolve({})) } }));
const api = jest.requireMock('@/lib/api') as { notifications: { registerDevice: jest.Mock; unregisterDevice: jest.Mock } };
const notificationsMock = Notifications as unknown as Record<'getPermissionsAsync' | 'requestPermissionsAsync', jest.Mock>;

const response = (data: unknown) => ({ notification: { request: { content: { data } } } }) as unknown as Notifications.NotificationResponse;

beforeEach(() => jest.clearAllMocks());

describe('push registration (N4/N5)', () => {
  it('registers this device token with its install id', async () => {
    expect(await registerForPush()).toEqual({ ok: true });
    expect(api.notifications.registerDevice).toHaveBeenCalledWith({ deviceId: 'device-0001', pushToken: 'ExponentPushToken[test-token-0000001]', platform: 'ios' });
  });

  it('asks once and stops when permission is denied', async () => {
    notificationsMock.getPermissionsAsync.mockResolvedValueOnce({ status: 'undetermined', granted: false });
    notificationsMock.requestPermissionsAsync.mockResolvedValueOnce({ status: 'denied', granted: false });

    expect(await registerForPush()).toEqual({ ok: false, reason: 'denied' });
    expect(api.notifications.registerDevice).not.toHaveBeenCalled();
  });

  it('needs a physical device', async () => {
    (Device as { isDevice: boolean }).isDevice = false;
    try {
      expect(await registerForPush()).toEqual({ ok: false, reason: 'simulator' });
    } finally {
      (Device as { isDevice: boolean }).isDevice = true;
    }
  });

  it('unregisters best effort', async () => {
    api.notifications.unregisterDevice.mockRejectedValueOnce(new Error('offline'));
    await expect(unregisterPush()).resolves.toBeUndefined();
    expect(api.notifications.unregisterDevice).toHaveBeenCalledWith('device-0001');
  });
});

describe('tapping a push', () => {
  it('opens the item deep link', () => {
    openFromNotification(response({ deepLink: '/position/NVDA', notificationId: 'n1' }));
    expect(router.push).toHaveBeenCalledWith('/position/NVDA');
  });

  it.each([[null], [{}], [{ deepLink: 'https://evil.example' }], [{ deepLink: '//evil.example' }], [{ deepLink: 42 }]])('ignores %j', (data) => {
    expect(deepLinkFrom(data)).toBeNull();
    openFromNotification(response(data));
    expect(router.push).not.toHaveBeenCalled();
  });
});
