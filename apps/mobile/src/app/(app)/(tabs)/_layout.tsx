import { color, type } from '@astryx/design-tokens';
import { Tabs } from 'expo-router';

// Five tabs; the active tab is shown by colour only (design). Custom <TabBar/> replaces this in Phase 1.
export default function TabsLayout() {
  return (
    <Tabs
      screenOptions={{
        headerShown: false,
        tabBarStyle: { backgroundColor: color.bar, borderTopColor: color.line },
        tabBarActiveTintColor: color.text,
        tabBarInactiveTintColor: color.text3,
        tabBarLabelStyle: type.caption,
        tabBarIconStyle: { display: 'none' },
      }}
    >
      <Tabs.Screen name="index" options={{ title: 'Home' }} />
      <Tabs.Screen name="discover" options={{ title: 'Discover' }} />
      <Tabs.Screen name="portfolio" options={{ title: 'Portfolio' }} />
      <Tabs.Screen name="strategies" options={{ title: 'Strategies' }} />
      <Tabs.Screen name="activity" options={{ title: 'Activity' }} />
    </Tabs>
  );
}
