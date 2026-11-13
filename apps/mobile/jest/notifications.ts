// expo-notifications without native modules: permission granted, a fixed token, listeners that never fire.
// Push tests override these per case.
export const getPermissionsAsync = jest.fn(() => Promise.resolve({ status: 'granted', granted: true }));
export const requestPermissionsAsync = jest.fn(() => Promise.resolve({ status: 'granted', granted: true }));
export const getExpoPushTokenAsync = jest.fn(() => Promise.resolve({ type: 'expo', data: 'ExponentPushToken[test-token-0000001]' }));
export const setNotificationHandler = jest.fn();
export const setNotificationChannelAsync = jest.fn(() => Promise.resolve(null));
export const addNotificationResponseReceivedListener = jest.fn(() => ({ remove: jest.fn() }));
export const getLastNotificationResponseAsync = jest.fn(() => Promise.resolve(null));
export const AndroidImportance = { DEFAULT: 3, HIGH: 4 };
