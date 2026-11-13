// Expo Router's testing library re-mocks Reanimated with `react-native-reanimated/mock`, so both resolve to our stub.
jest.mock('react-native-reanimated', () => require('./jest/reanimated'));
jest.mock('react-native-reanimated/mock', () => require('./jest/reanimated'));

jest.mock('@gorhom/bottom-sheet', () => require('@gorhom/bottom-sheet/mock'));

// Charts (T-048): no native Skia, worklets or gesture recognisers in Jest.
jest.mock('@shopify/react-native-skia', () => require('./jest/skia'));
jest.mock('@microsoft/signalr', () => require('./jest/signalr'));
jest.mock('react-native-worklets', () => ({ scheduleOnRN: (fn: (...args: unknown[]) => void, ...args: unknown[]) => fn(...args) }));
jest.mock('react-native-gesture-handler', () => ({
  ...jest.requireActual('react-native-gesture-handler/jestSetup'),
  GestureDetector: ({ children }: { children: unknown }) => children,
  GestureHandlerRootView: ({ children }: { children: unknown }) => children,
  usePanGesture: () => ({}),
}));
jest.mock('expo-haptics', () => ({ selectionAsync: jest.fn(), impactAsync: jest.fn(), notificationAsync: jest.fn(), ImpactFeedbackStyle: {}, NotificationFeedbackType: {} }));

// Connection banner (T-050): NetInfo's own mock reports a connected wifi network.
jest.mock('@react-native-community/netinfo', () => require('@react-native-community/netinfo/jest/netinfo-mock.js'));

// Discover (T-067): AsyncStorage's own mock; FlashList renders through FlatList so rows appear in tests.
jest.mock('@react-native-async-storage/async-storage', () => require('@react-native-async-storage/async-storage/jest/async-storage-mock'));
jest.mock('@shopify/flash-list', () => {
  const { FlatList } = jest.requireActual('react-native');
  return { FlashList: FlatList };
});

// Push (T-101): no native notification module or device info in Jest.
jest.mock('expo-notifications', () => require('./jest/notifications'));
jest.mock('expo-device', () => ({ isDevice: true }));
