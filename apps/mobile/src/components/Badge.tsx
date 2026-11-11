import { color, radius, space, type as typeScale } from '@astryx/design-tokens';
import { StyleSheet, Text, type TextStyle, View } from 'react-native';

export type SignalType = 'breakout' | 'momentum' | 'pullback' | 'unusual_volume' | 'earnings';

export type BadgeTone = 'signal' | 'gain' | 'loss' | 'pending' | 'neutral' | 'violet' | 'teal' | 'pink';

const tones: Record<BadgeTone, { fg: string; bg: string }> = {
  signal: { fg: color.signal, bg: 'rgba(138,180,255,0.14)' },
  gain: { fg: color.gain, bg: 'rgba(61,214,140,0.12)' },
  loss: { fg: color.loss, bg: 'rgba(255,107,107,0.12)' },
  pending: { fg: color.pending, bg: 'rgba(242,181,68,0.12)' },
  neutral: { fg: color.text2, bg: color.surface2 },
  violet: { fg: color.series[3], bg: 'rgba(196,155,255,0.14)' },
  teal: { fg: color.series[2], bg: 'rgba(95,212,208,0.12)' },
  pink: { fg: color.series[4], bg: 'rgba(229,143,181,0.14)' },
};

/** Signal colours from design 03 · Badges. None is green or red, so a signal never reads as P&L. */
export const signalBadge: Record<SignalType, { label: string; tone: BadgeTone }> = {
  breakout: { label: 'Breakout', tone: 'signal' },
  momentum: { label: 'Momentum', tone: 'violet' },
  pullback: { label: 'Pullback', tone: 'teal' },
  unusual_volume: { label: 'Unusual volume', tone: 'pending' },
  earnings: { label: 'Earnings', tone: 'pink' },
};

export interface BadgeProps {
  label: string;
  tone?: BadgeTone;
  /** Mono uppercase, for status badges such as NEW and "1.2% to stop". */
  mono?: boolean;
  /** Outlined with a leading dot, for the PAPER chip. */
  outlined?: boolean;
  testID?: string;
}

export function Badge({ label, tone = 'neutral', mono = false, outlined = false, testID }: BadgeProps) {
  const t = tones[tone];
  return (
    <View
      testID={testID}
      style={[styles.badge, outlined ? { borderWidth: 1, borderColor: t.fg, backgroundColor: 'transparent' } : { backgroundColor: t.bg }]}
    >
      {outlined ? <View style={[styles.dot, { backgroundColor: t.fg }]} /> : null}
      <Text style={[mono ? styles.mono : styles.text, { color: t.fg }]}>{mono ? label.toUpperCase() : label}</Text>
    </View>
  );
}

export function SignalBadge({ type }: { type: SignalType }) {
  const { label, tone } = signalBadge[type];
  return <Badge label={label} tone={tone} />;
}

/** The PAPER chip shown on Home and Portfolio (design safety rule 4). */
export function PaperBadge() {
  return <Badge label="Paper" tone="pending" mono outlined />;
}

const styles = StyleSheet.create({
  badge: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: space[1],
    alignSelf: 'flex-start',
    borderRadius: radius.badge,
    paddingHorizontal: 6,
    paddingVertical: 2,
  },
  text: { ...(typeScale.caption as TextStyle), fontFamily: 'Geist_500Medium' },
  mono: { ...(typeScale.overline as TextStyle), letterSpacing: 0.4 },
  dot: { width: 6, height: 6, borderRadius: 3 },
});
