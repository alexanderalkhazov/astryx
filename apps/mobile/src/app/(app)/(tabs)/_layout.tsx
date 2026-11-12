import { Tabs } from 'expo-router';
import { useReducedMotion } from 'react-native-reanimated';

import { TabBar } from '@/components/Navigation';
import { tabTransition } from '@/lib/motion';

// Five tabs (design 03 · Navigation); the custom bar shows the active tab by colour only.
export default function TabsLayout() {
  const reduceMotion = useReducedMotion();
  return (
    <Tabs screenOptions={{ headerShown: false, ...tabTransition(reduceMotion) }} tabBar={(props) => <TabBar {...props} />}>
      <Tabs.Screen name="index" options={{ title: 'Home' }} />
      <Tabs.Screen name="discover" options={{ title: 'Discover' }} />
      <Tabs.Screen name="portfolio" options={{ title: 'Portfolio' }} />
      <Tabs.Screen name="strategies" options={{ title: 'Strategies' }} />
      <Tabs.Screen name="activity" options={{ title: 'Activity' }} />
    </Tabs>
  );
}
