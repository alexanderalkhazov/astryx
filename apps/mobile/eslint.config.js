// ESLint (T-003): Expo's flat config, Prettier owns formatting, plus the colour-meaning rule (SYSTEM_DESIGN.md §8.6).
const { defineConfig } = require('eslint/config');
const expoConfig = require('eslint-config-expo/flat');
const prettierConfig = require('eslint-config-prettier');

module.exports = defineConfig([
  expoConfig,
  prettierConfig,
  { ignores: ['dist/**', 'ios/**', 'android/**', '.expo/**', 'coverage/**', 'expo-env.d.ts'] },
  {
    rules: {
      // React Native <Text> renders characters literally; there is no HTML to escape.
      'react/no-unescaped-entities': 'off',
      // TypeScript already resolves imports; decimal.js documents `import Decimal from 'decimal.js'`.
      'import/namespace': 'off',
      'import/no-named-as-default': 'off',
      // React Compiler readiness rules. The compiler is not enabled, and the flagged code is deliberate:
      // drafts reset when a sheet opens, and local accumulators inside a render-time map.
      'react-hooks/set-state-in-effect': 'off',
      'react-hooks/immutability': 'off',
    },
  },
  {
    // Jest setup and module mocks load modules lazily with require().
    files: ['jest.setup.ts', 'jest/**'],
    rules: { '@typescript-eslint/no-require-imports': 'off' },
  },
  {
    // Maestro runScript files: the runner injects http, json, output and the flow's env variables.
    files: ['e2e/scripts/**/*.js'],
    languageOptions: {
      globals: {
        http: 'readonly',
        json: 'readonly',
        output: 'writable',
        API_URL: 'readonly',
        EMAIL: 'readonly',
        PASSWORD: 'readonly',
        FAULTS: 'readonly',
      },
    },
  },
  {
    // Green means money made or an order executed. Screens and features get it only through the design-system
    // primitives in src/components (Text tone="gain", Metric, Badge, StatusBanner executed, the charts), never directly.
    files: ['src/**/*.{ts,tsx}'],
    ignores: ['src/components/**'],
    rules: {
      'no-restricted-syntax': [
        'error',
        {
          selector: "MemberExpression[object.name='color'][property.name='gain']",
          message:
            'color.gain is reserved for P&L and executed states: use a src/components primitive with tone="gain", or color.signal / color.pending.',
        },
      ],
    },
  },
]);
