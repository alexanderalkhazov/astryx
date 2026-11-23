import { color } from '@astryx/design-tokens';
import { Redirect, Stack, useGlobalSearchParams, usePathname, useSegments } from 'expo-router';
import { useEffect } from 'react';
import { useReducedMotion } from 'react-native-reanimated';

import { useDisclaimerAccepted } from '@/features/onboarding/disclaimer';
import { useGoal } from '@/features/onboarding/goal';
import { useInvestorProfile } from '@/features/onboarding/profile';
import { usePushNotifications } from '@/features/push/usePushNotifications';
import { useScreenProtection } from '@/features/security/useScreenProtection';
import { useRealtimeConnection } from '@/features/realtime/useRealtimeConnection';
import { stackTransition } from '@/lib/motion';
import { clearPendingLink, currentHref, stashPendingLink } from '@/state/pendingLink';
import { useSession } from '@/state/session';

// Auth gate (SYSTEM_DESIGN.md §8.3): without a session, remember where the user was headed and send them to sign in.
export default function AppLayout() {
  const status = useSession((s) => s.status);
  const signedOutByUser = useSession((s) => s.signedOutByUser);
  const pathname = usePathname();
  const segments = useSegments();
  const params = useGlobalSearchParams();
  const reduceMotion = useReducedMotion();
  const userId = useSession((s) => s.user?.id);
  const disclaimer = useDisclaimerAccepted(status === 'signedIn' ? userId : undefined);
  const profile = useInvestorProfile(status === 'signedIn' ? userId : undefined);
  const goal = useGoal(status === 'signedIn' ? userId : undefined);
  useRealtimeConnection();
  usePushNotifications();
  useScreenProtection(status === 'signedIn');

  useEffect(() => {
    if (status === 'signedOut' && !signedOutByUser) stashPendingLink(currentHref(pathname, segments, params));
    // Signed in means the (auth) layout has just replayed the link, if there was one: replay it only once.
    if (status === 'signedIn') clearPendingLink();
    // Only the screen shown at the moment the session changed matters.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [status]);

  if (status === 'unknown') return null; // splash is still up while the session is restored
  if (status === 'signedOut') return <Redirect href="/sign-in" />;
  // T-118: the disclaimer comes first, once per account on this device.
  if (disclaimer === false && pathname !== '/welcome') return <Redirect href="/welcome" />;
  // T-162: then the three onboarding questions, once.
  if (disclaimer === true && profile === null && pathname !== '/onboarding' && pathname !== '/welcome')
    return <Redirect href="/onboarding" />;
  // T-190: then what they want to do first, once.
  const onboarding = ['/welcome', '/onboarding', '/start', '/privacy'];
  if (disclaimer === true && profile && goal === null && !onboarding.includes(pathname)) return <Redirect href="/start" />;

  return (
    <Stack
      screenOptions={{
        headerShown: false,
        contentStyle: { backgroundColor: color.bg },
        ...stackTransition(reduceMotion),
      }}
    >
      <Stack.Screen name="(tabs)" />
      <Stack.Screen name="welcome" options={{ gestureEnabled: false, animation: 'fade' }} />
      <Stack.Screen name="onboarding" options={{ gestureEnabled: false, animation: 'fade' }} />
      <Stack.Screen name="start" options={{ gestureEnabled: false, animation: 'fade' }} />
      <Stack.Screen name="search" options={{ presentation: 'fullScreenModal', animation: 'fade' }} />
    </Stack>
  );
}
