// "Graphite terminal" design tokens — single source of truth (SYSTEM_DESIGN.md §8.5).
// Colour carries meaning only: gain/loss for P&L, signal for research, pending for anything not executed.

export const color = {
  bg: '#090B0E',
  bar: '#0C0F13',
  surface1: '#101318',
  surface2: '#151920',
  line: '#1C2129',
  lineStrong: '#2C333D',
  text: '#ECEFF3',
  text2: '#A3ABB6',
  text3: '#7D8692',
  gain: '#3DD68C',
  loss: '#FF6B6B',
  signal: '#8AB4FF',
  pending: '#F2B544',
  series: ['#8AB4FF', '#F2B544', '#5FD4D0', '#C49BFF', '#E58FB5', '#9BD47A', '#D9A27A'],
  benchmark: '#7D8692',
  ripple: 'rgba(255,255,255,0.08)',
} as const;

/**
 * One font family per weight: custom fonts on Android ignore `fontWeight`, so each style names its exact face.
 * The names match the faces loaded from @expo-google-fonts/geist and /geist-mono.
 */
export const font = {
  sans: { regular: 'Geist_400Regular', medium: 'Geist_500Medium', semibold: 'Geist_600SemiBold' },
  mono: { medium: 'GeistMono_500Medium', semibold: 'GeistMono_600SemiBold' },
} as const;

/** Prices, P&L, quantities and tickers line up in columns (design: tabular figures throughout). */
const tabular = { fontVariant: ['tabular-nums'] } as const;

export const type = {
  display: { fontFamily: font.sans.semibold, fontSize: 40, letterSpacing: -1.4, ...tabular }, // 40 / 600 / −3.5%
  priceXl: { fontFamily: font.mono.semibold, fontSize: 32, ...tabular }, // Mono 32 / 600
  title: { fontFamily: font.sans.semibold, fontSize: 22, letterSpacing: -0.44 }, // 22 / 600 / −2%
  headline: { fontFamily: font.sans.semibold, fontSize: 19 },
  section: { fontFamily: font.sans.semibold, fontSize: 15 },
  body: { fontFamily: font.sans.regular, fontSize: 14, lineHeight: 22 },
  data: { fontFamily: font.mono.medium, fontSize: 14, ...tabular }, // Mono 13–14 / 500
  dataSm: { fontFamily: font.mono.medium, fontSize: 13, ...tabular },
  caption: { fontFamily: font.sans.regular, fontSize: 12 },
  overline: { fontFamily: font.mono.semibold, fontSize: 11, letterSpacing: 0.88, textTransform: 'uppercase' }, // +8%
} as const;

export const space = { 1: 4, 2: 8, 3: 12, 4: 16, 5: 22, 6: 32 } as const;
export const radius = { badge: 4, chip: 8, card: 12, button: 12, dialog: 18, sheet: 20 } as const;
export const size = {
  touch: 44,
  button: 52,
  hold: 56,
  row: 60,
  rowLg: 64,
  tabBar: 56,
  contentMax: 358,
  gutter: 16,
  opportunityCard: 236,
  cardSnap: 246,
} as const;

export const elevation = {
  e0: { borderWidth: 1, borderColor: color.line },
  e1: { shadowColor: '#000', shadowOpacity: 0.5, shadowRadius: 16, shadowOffset: { width: 0, height: 12 } },
  e2: { shadowColor: '#000', shadowOpacity: 0.55, shadowRadius: 30, shadowOffset: { width: 0, height: 24 } },
} as const;

export const motion = {
  easing: [0.2, 0.8, 0.2, 1] as const,
  push: 320,
  sheet: 360,
  backdrop: 300,
  dialog: 280,
  dialogScaleFrom: 0.94,
  tab: 200,
  tabRise: 6,
  hold: 1200,
  skeleton: 1400,
  skeletonStagger: 120,
  toast: 3800,
  scrubHapticThrottle: 30,
} as const;

export const chart = { pricePane: 210, volumePane: 40, indicatorPane: 64, axisGutter: 44 } as const;
