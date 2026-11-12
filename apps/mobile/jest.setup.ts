// Expo Router's testing library re-mocks Reanimated with `react-native-reanimated/mock`, so both resolve to our stub.
jest.mock('react-native-reanimated', () => require('./jest/reanimated'));
jest.mock('react-native-reanimated/mock', () => require('./jest/reanimated'));

jest.mock('@gorhom/bottom-sheet', () => require('@gorhom/bottom-sheet/mock'));
