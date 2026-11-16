import { renderHook } from '@testing-library/react-native';
import * as ScreenCapture from 'expo-screen-capture';
import { Platform } from 'react-native';

import { useScreenProtection } from '../useScreenProtection';

const sc = ScreenCapture as unknown as Record<
  'enableAppSwitcherProtectionAsync' | 'disableAppSwitcherProtectionAsync' | 'preventScreenCaptureAsync' | 'allowScreenCaptureAsync',
  jest.Mock
>;

beforeEach(() => jest.clearAllMocks());

describe('useScreenProtection', () => {
  it('blurs the iOS app-switcher snapshot while signed in, and stops on sign-out', async () => {
    Platform.OS = 'ios';
    const { rerender } = await renderHook(({ on }: { on: boolean }) => useScreenProtection(on), { initialProps: { on: true } });
    expect(sc.enableAppSwitcherProtectionAsync).toHaveBeenCalledWith(0.9);

    await rerender({ on: false });
    expect(sc.disableAppSwitcherProtectionAsync).toHaveBeenCalled();
  });

  it('sets FLAG_SECURE on Android', async () => {
    Platform.OS = 'android';
    const { unmount } = await renderHook(() => useScreenProtection(true));
    expect(sc.preventScreenCaptureAsync).toHaveBeenCalledWith('balances');

    await unmount();
    expect(sc.allowScreenCaptureAsync).toHaveBeenCalledWith('balances');
    Platform.OS = 'ios';
  });

  it('does nothing while signed out', async () => {
    await renderHook(() => useScreenProtection(false));
    expect(sc.enableAppSwitcherProtectionAsync).not.toHaveBeenCalled();
    expect(sc.preventScreenCaptureAsync).not.toHaveBeenCalled();
  });
});
