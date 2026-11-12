import type { IndicatorSnapshot } from '@astryx/api-client';
import { color, space } from '@astryx/design-tokens';
import { StyleSheet, View } from 'react-native';

import { Badge } from '@/components/Badge';
import { EmptyState, SkeletonRow } from '@/components/Feedback';
import { Text } from '@/components/Text';
import { formatPct, formatSessionDate } from '@/lib/format';

const MISSING = '—';

type Row = { label: string; value: (s: IndicatorSnapshot) => string | null };

const pct = (v: string | null) => (v === null ? null : formatPct(v, { signed: true }));

const ROWS: Row[] = [
  { label: 'RSI (14)', value: (s) => s.values.rsi14 },
  { label: 'MACD (12, 26, 9)', value: (s) => s.values.macd },
  { label: 'MACD signal', value: (s) => s.values.macdSignal },
  { label: 'MACD histogram', value: (s) => s.values.macdHist },
  { label: 'ATR (14)', value: (s) => s.values.atr14 },
  { label: 'SMA 20', value: (s) => s.values.sma20 },
  { label: 'SMA 50', value: (s) => s.values.sma50 },
  { label: 'EMA 20', value: (s) => s.values.ema20 },
  { label: 'Relative volume (20)', value: (s) => (s.values.relVolume20 === null ? null : `${s.values.relVolume20}×`) },
  { label: 'Relative strength', value: (s) => s.values.relStrength },
  { label: 'Momentum (20 days)', value: (s) => pct(s.values.momentum20Pct) },
  { label: 'Volatility (20 days, annual)', value: (s) => (s.values.volatility20Ann === null ? null : formatPct(s.values.volatility20Ann)) },
  { label: 'Trend', value: (s) => s.values.trend },
];

const QUALITY: Record<IndicatorSnapshot['dataQuality'], { label: string; tone: 'gain' | 'pending' | 'loss' | 'neutral' } | null> = {
  ok: null,
  gaps: { label: 'Gaps in history', tone: 'pending' },
  stale: { label: 'Stale data', tone: 'loss' },
  insufficient: { label: 'Short history', tone: 'pending' },
};

/** Technicals tab (design 05, SYSTEM_DESIGN.md §8.4.3): the M7 snapshot as a table, with the bar it is as of. */
export function TechnicalsTab({ snapshot, pending, failed, onRetry }: { snapshot: IndicatorSnapshot | undefined; pending: boolean; failed: boolean; onRetry: () => void }) {
  if (pending) return <SkeletonRow />;
  if (failed || !snapshot) {
    return <EmptyState title="Indicators unavailable" body="They couldn't be computed right now. Try again shortly." action={{ label: 'Retry', onPress: onRetry }} />;
  }

  const quality = QUALITY[snapshot.dataQuality];
  return (
    <View testID="technicals-tab">
      <View style={styles.meta}>
        <Text.Caption tone="tertiary">{`As of ${formatSessionDate(snapshot.asOfBar)} close · ${snapshot.barCount} daily bars`}</Text.Caption>
        {quality ? <Badge label={quality.label} tone={quality.tone} /> : null}
      </View>
      {ROWS.map((row, index) => {
        const value = row.value(snapshot);
        return (
          <View key={row.label} style={[styles.row, index > 0 && styles.divider]} accessible accessibilityLabel={`${row.label}, ${value ?? 'not enough history'}`}>
            <Text.Body tone="secondary">{row.label}</Text.Body>
            <Text.Data tone={value === null ? 'tertiary' : 'primary'}>{value ?? MISSING}</Text.Data>
          </View>
        );
      })}
      {snapshot.insufficient.length > 0 ? (
        <Text.Caption tone="tertiary" style={styles.note}>
          — Not enough history yet
        </Text.Caption>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  meta: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: space[2], paddingBottom: space[2] },
  row: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', minHeight: 44, gap: space[3] },
  divider: { borderTopWidth: 1, borderTopColor: color.line },
  note: { marginTop: space[3] },
});
