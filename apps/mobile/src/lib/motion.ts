import { motion } from '@astryx/design-tokens';
import type { Stack, Tabs } from 'expo-router';
import type { ComponentProps } from 'react';
import { Easing, Platform } from 'react-native';

// Expo Router vendors React Navigation, so the option types come from its layouts.
// `screenOptions` also accepts a function form; keep only the object.
type ObjectOptions<T> = Exclude<NonNullable<T>, (...args: never[]) => unknown>;
type StackScreenOptions = ObjectOptions<ComponentProps<typeof Stack>['screenOptions']>;
type TabScreenOptions = ObjectOptions<ComponentProps<typeof Tabs>['screenOptions']>;

const ease = Easing.bezier(...motion.easing);

/**
 * Pushed screens (SYSTEM_DESIGN.md §8.12): push from the right with edge-swipe back on iOS, fade-through on Android.
 * Reduce Motion swaps movement for a cross-fade (iOS) or no animation (Android).
 */
export function stackTransition(reduceMotion: boolean): StackScreenOptions {
  if (Platform.OS === 'ios') {
    return { animation: reduceMotion ? 'fade' : 'slide_from_right', animationDuration: motion.push, gestureEnabled: true };
  }
  return { animation: reduceMotion ? 'none' : 'fade', animationDuration: motion.push };
}

/** Tab change (design motion.tab / tabRise): the incoming scene fades in while rising 6 pt; Reduce Motion cuts instantly. */
export function tabTransition(reduceMotion: boolean): Pick<TabScreenOptions, 'sceneStyleInterpolator' | 'transitionSpec'> {
  return {
    transitionSpec: { animation: 'timing', config: { duration: reduceMotion ? 0 : motion.tab, easing: ease } },
    sceneStyleInterpolator: ({ current }) => ({
      sceneStyle: {
        opacity: current.progress.interpolate({ inputRange: [-1, 0, 1], outputRange: [0, 1, 0] }),
        transform: reduceMotion
          ? []
          : [{ translateY: current.progress.interpolate({ inputRange: [-1, 0, 1], outputRange: [motion.tabRise, 0, motion.tabRise] }) }],
      },
    }),
  };
}
