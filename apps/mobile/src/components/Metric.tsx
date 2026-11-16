import { color, radius, space, type as typeScale } from '@astryx/design-tokens';
import type { ReactNode } from 'react';
import { StyleSheet, Text, type TextStyle, View } from 'react-native';

import { moneyAccessibilityLabel, splitMoney } from '@/lib/format';
import { toDecimal, type DecimalString } from '@/lib/decimal';

export interface MetricHeroProps {
  label: string;
  /** Decimal string, e.g. "118181.30". */
  value: DecimalString;
  children?: ReactNode;
}

/** Hero value with dimmed cents (design 03 · Metric displays: "$118,181.30"). */
export function MetricHero({ label, value, children }: MetricHeroProps) {
  const { main, cents } = splitMoney(value);
  return (
    <View style={{ gap: space[1] }}>
      <Text style={styles.label}>{label}</Text>
      <Text style={styles.hero} accessibilityLabel={`${label}: ${moneyAccessibilityLabel(value).replace(/^plus /, '')}`}>
        {main}
        <Text style={styles.cents}>{cents}</Text>
      </Text>
      {children}
    </View>
  );
}

export type MetricTone = 'default' | 'gain' | 'loss';

export interface MetricTileProps {
  label: string;
  /** Pre-formatted value (use the format helpers). */
  value: string;
  tone?: MetricTone;
  caption?: string;
}

const toneColor: Record<MetricTone, string> = { default: color.text, gain: color.gain, loss: color.loss };

/** Outcome tile, e.g. "Max potential loss −$630.80 · 0.53% of equity". */
export function MetricTile({ label, value, tone = 'default', caption }: MetricTileProps) {
  return (
    <View style={styles.tile} accessible accessibilityLabel={`${label}: ${value}${caption ? `, ${caption}` : ''}`}>
      <Text style={styles.label}>{label}</Text>
      <Text style={[styles.tileValue, { color: toneColor[tone] }]}>{value}</Text>
      {caption ? <Text style={styles.caption}>{caption}</Text> : null}
    </View>
  );
}

export interface MetricGridProps {
  items: readonly { label: string; value: string; tone?: MetricTone }[];
}

/** Compact metric strip with dividers, e.g. CAGR · Sharpe · Max DD · PF. */
export function MetricGrid({ items }: MetricGridProps) {
  return (
    <View style={styles.grid}>
      {items.map((item, index) => (
        <View
          key={item.label}
          style={[styles.cell, index > 0 ? styles.cellDivider : null]}
          accessible
          accessibilityLabel={`${item.label}: ${item.value}`}
        >
          <Text style={styles.label}>{item.label}</Text>
          <Text style={[styles.gridValue, { color: toneColor[item.tone ?? 'default'] }]}>{item.value}</Text>
        </View>
      ))}
    </View>
  );
}

export interface ScoreBreakdownBarProps {
  label: string;
  /** Decimal strings. */
  value: DecimalString;
  max: DecimalString;
}

/** One deterministic score component, e.g. "Pattern quality ███░ 24/30" (SYSTEM_DESIGN.md §13.4). */
export function ScoreBreakdownBar({ label, value, max }: ScoreBreakdownBarProps) {
  const ratio = toDecimal(max).isZero() ? 0 : Math.min(1, Math.max(0, Number(toDecimal(value).dividedBy(toDecimal(max)).toFixed(4)))); // pixel width only
  return (
    <View style={styles.score} accessible accessibilityLabel={`${label}: ${value} of ${max}`}>
      <Text style={[styles.body, { flex: 1 }]}>{label}</Text>
      <View style={styles.track}>
        <View style={[styles.fill, { width: `${ratio * 100}%` }]} />
      </View>
      <Text style={styles.scoreValue}>
        {value}/{max}
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  label: { ...(typeScale.caption as TextStyle), color: color.text2 },
  hero: { ...(typeScale.display as TextStyle), color: color.text },
  cents: { ...(typeScale.display as TextStyle), fontSize: 24, color: color.text3 },
  tile: { flex: 1, gap: 2, padding: space[3], borderRadius: radius.card, borderWidth: 1, borderColor: color.line },
  tileValue: { ...(typeScale.data as TextStyle), fontSize: 18, fontFamily: 'GeistMono_600SemiBold' },
  caption: { ...(typeScale.caption as TextStyle), color: color.text3 },
  grid: { flexDirection: 'row', borderRadius: radius.card, borderWidth: 1, borderColor: color.line, overflow: 'hidden' },
  cell: { flex: 1, gap: 2, paddingVertical: space[2], paddingHorizontal: space[3] },
  cellDivider: { borderLeftWidth: 1, borderLeftColor: color.line },
  gridValue: { ...(typeScale.data as TextStyle), fontFamily: 'GeistMono_600SemiBold' },
  score: { flexDirection: 'row', alignItems: 'center', gap: space[3] },
  body: { ...(typeScale.body as TextStyle), lineHeight: undefined, color: color.text2 },
  track: { flex: 1.4, height: 4, borderRadius: 2, backgroundColor: color.line, overflow: 'hidden' },
  fill: { height: 4, borderRadius: 2, backgroundColor: color.signal },
  scoreValue: { ...(typeScale.dataSm as TextStyle), color: color.text, minWidth: 44, textAlign: 'right' },
});
