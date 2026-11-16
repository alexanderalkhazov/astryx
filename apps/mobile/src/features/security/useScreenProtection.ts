import * as ScreenCapture from 'expo-screen-capture';
import { useEffect } from 'react';
import { Platform } from 'react-native';

/**
 * Balances stay out of app-switcher snapshots while signed in (SYSTEM_DESIGN.md §18.6, T-115). Almost every signed-in
 * screen shows money, and tabs stay mounted, so this covers the signed-in app rather than toggling per screen.
 * iOS blurs the snapshot; Android sets FLAG_SECURE, which also blocks screenshots there.
 */
export function useScreenProtection(enabled: boolean): void {
  useEffect(() => {
    if (!enabled) return undefined;
    if (Platform.OS === 'ios') {
      void ScreenCapture.enableAppSwitcherProtectionAsync(0.9);
      return () => void ScreenCapture.disableAppSwitcherProtectionAsync();
    }

    void ScreenCapture.preventScreenCaptureAsync('balances');
    return () => void ScreenCapture.allowScreenCaptureAsync('balances');
  }, [enabled]);
}
