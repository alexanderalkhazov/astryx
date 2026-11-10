import { color, motion } from '@astryx/design-tokens';
import { Stack } from 'expo-router';

export default function AppLayout() {
  return (
    <Stack
      screenOptions={{
        headerShown: false,
        contentStyle: { backgroundColor: color.bg },
        animationDuration: motion.push,
      }}
    >
      <Stack.Screen name="(tabs)" />
      <Stack.Screen name="search" options={{ presentation: 'fullScreenModal', animation: 'fade' }} />
    </Stack>
  );
}
