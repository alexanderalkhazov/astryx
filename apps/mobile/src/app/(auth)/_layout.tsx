import { color } from '@astryx/design-tokens';
import { type Href, Redirect, Stack, useSegments } from 'expo-router';

import { usePendingLink } from '@/state/pendingLink';
import { useSession } from '@/state/session';

/** Email links work whether or not this device is signed in. */
const LINK_SCREENS = new Set(['verify-email', 'reset-password']);

// PROPOSED — authentication screens are not in the design (SYSTEM_DESIGN.md §8.2).
// Signed in: leave for the deep link stashed by the (app) gate (which forgets it on arrival), or Home.
export default function AuthLayout() {
  const status = useSession((s) => s.status);
  const pendingLink = usePendingLink((s) => s.href);
  const screen = useSegments()[1];

  if (status === 'unknown') return null;
  if (status === 'signedIn' && !(screen !== undefined && LINK_SCREENS.has(screen))) return <Redirect href={(pendingLink ?? '/') as Href} />;
  return <Stack screenOptions={{ headerShown: false, contentStyle: { backgroundColor: color.bg } }} />;
}
