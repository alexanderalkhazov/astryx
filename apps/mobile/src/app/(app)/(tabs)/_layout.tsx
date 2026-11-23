import { Tabs } from 'expo-router';
import { useReducedMotion } from 'react-native-reanimated';

import { TabBar } from '@/components/Navigation';
import { useSignalBadge } from '@/features/discover/useSignals';
import { useViewMode } from '@/features/plain/mode';
import { tabTransition } from '@/lib/motion';
import { useSession } from '@/state/session';

const HIDDEN = { tabBarItemStyle: { display: 'none' } } as const;

// Pro: five tabs (design 03 · Navigation). Simple (T-194): Home, Invest, Practice, Learn; the Pro screens stay reachable
// from links, only their tab buttons are hidden. The custom bar shows the active tab by colour only.
export default function TabsLayout() {
  const reduceMotion = useReducedMotion();
  const signals = useSignalBadge();
  const simple = useViewMode(useSession((s) => s.user?.id)) === 'simple';
  const pro = simple ? HIDDEN : {};
  const plain = simple ? {} : HIDDEN;
  return (
    <Tabs screenOptions={{ headerShown: false, ...tabTransition(reduceMotion) }} tabBar={(props) => <TabBar {...props} />}>
      <Tabs.Screen name="index" options={{ title: 'Home' }} />
      <Tabs.Screen name="invest" options={{ title: 'Invest', ...plain }} />
      <Tabs.Screen name="practice" options={{ title: 'Practice', ...plain }} />
      <Tabs.Screen name="learn" options={{ title: 'Learn', ...plain }} />
      <Tabs.Screen name="discover" options={{ title: 'Discover', tabBarBadge: signals, ...pro }} />
      <Tabs.Screen name="portfolio" options={{ title: 'Portfolio', ...pro }} />
      <Tabs.Screen name="strategies" options={{ title: 'Strategies', ...pro }} />
      <Tabs.Screen name="activity" options={{ title: 'Activity', ...pro }} />
    </Tabs>
  );
}
