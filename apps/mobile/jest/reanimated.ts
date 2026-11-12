// Reanimated 4 needs the native worklets runtime, and its bundled Jest mock still loads the real library, so tests
// use this stub of the APIs our components call. Values settle immediately; motion is verified on the simulator.
import { Text, View } from 'react-native';

const identity = (value: unknown) => value;

module.exports = {
  __esModule: true,
  // gesture-handler reads `default.createAnimatedComponent`; Expo Router's test mocks overwrite `default.call`.
  default: { View, Text, createAnimatedComponent: identity, call: () => undefined },
  View,
  Text,
  createAnimatedComponent: identity,
  useSharedValue: (initial: unknown) => ({ value: initial }),
  useAnimatedStyle: (factory: () => object) => factory(),
  withTiming: identity,
  withRepeat: identity,
  withDelay: (_delay: number, value: unknown) => value,
  Easing: { bezier: () => identity, inOut: () => identity, ease: identity, linear: identity },
  ReduceMotion: { System: 'system', Always: 'always', Never: 'never' },
  useReducedMotion: () => false,
  FadeInUp: undefined,
  FadeOutUp: undefined,
};
