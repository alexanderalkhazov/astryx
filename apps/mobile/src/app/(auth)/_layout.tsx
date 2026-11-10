import { color } from '@astryx/design-tokens';
import { Stack } from 'expo-router';

// PROPOSED — authentication screens are not in the design (SYSTEM_DESIGN.md §8.2).
export default function AuthLayout() {
  return <Stack screenOptions={{ headerShown: false, contentStyle: { backgroundColor: color.bg } }} />;
}
