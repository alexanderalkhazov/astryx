// Jest config for the Expo app (jest-expo preset) under pnpm's isolated node_modules.
module.exports = {
  preset: 'jest-expo',
  setupFiles: ['<rootDir>/jest.setup.ts'],
  // pnpm nests packages under node_modules/.pnpm/<id>/node_modules/<name>; allow the RN ecosystem through Babel.
  transformIgnorePatterns: [
    'node_modules/(?!(?:\\.pnpm/[^/]+/node_modules/)?((jest-)?react-native|@react-native(-community)?|expo(nent)?|@expo(nent)?/.*|@expo-google-fonts/.*|react-navigation|@react-navigation/.*|decimal\\.js-light|react-native-svg|standard-navigation))',
  ],
  moduleNameMapper: {
    '^@/(.*)$': '<rootDir>/src/$1',
    // Jest resolves Lucide's "react-native" export (an .mjs file it cannot require); use its CommonJS build instead.
    '^lucide-react-native$': require.resolve('lucide-react-native'),
  },
};
