import { color } from '@astryx/design-tokens';
import { Redirect, Stack, useGlobalSearchParams, usePathname, useSegments } from 'expo-router';
import { useEffect } from 'react';
import { useReducedMotion } from 'react-native-reanimated';

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

  useEffect(() => {
    if (status === 'signedOut' && !signedOutByUser) stashPendingLink(currentHref(pathname, segments, params));
    // Signed in means the (auth) layout has just replayed the link, if there was one: replay it only once.
    if (status === 'signedIn') clearPendingLink();
    // Only the screen shown at the moment the session changed matters.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [status]);

  if (status === 'unknown') return null; // splash is still up while the session is restored
  if (status === 'signedOut') return <Redirect href="/sign-in" />;

  return (
    <Stack
      screenOptions={{
        headerShown: false,
        contentStyle: { backgroundColor: color.bg },
        ...stackTransition(reduceMotion),
      }}
    >
      <Stack.Screen name="(tabs)" />
      <Stack.Screen name="search" options={{ presentation: 'fullScreenModal', animation: 'fade' }} />
    </Stack>
  );
}
