import type { FullMetrics, HeadlineMetrics, StrategyStatus, StrategySummary } from '@astryx/api-client';
import { color, radius, space } from '@astryx/design-tokens';
import Decimal from 'decimal.js-light';
import { Pressable, StyleSheet, View } from 'react-native';

import { Badge, type BadgeTone } from '@/components/Badge';
import { Sparkline } from '@/components/chart/Sparkline';
import { MetricGrid } from '@/components/Metric';
import { Text } from '@/components/Text';
import { formatPct, formatRatio } from '@/lib/format';

/** Backtests are history replayed by rules, never a forecast (SYSTEM_DESIGN.md §7.8). Shown wherever results are. */
export const ILLUSTRATIVE_COPY = 'Backtests replay history with fixed rules. Results are historical and illustrative, not a forecast of future returns.';

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/** "2015-01-01" → "Jan 2015". */
export function formatMonthYear(isoDate: string): string {
  const [year, month] = isoDate.split('-');
  return `${MONTHS[Number(month) - 1]} ${year}`;
}

const STATUS: Record<StrategyStatus, { label: string; tone: BadgeTone }> = {
  active: { label: 'Active', tone: 'gain' },
  active_guarded: { label: 'Active · guard', tone: 'pending' },
  paused: { label: 'Paused', tone: 'neutral' },
};

export function StrategyStatusBadge({ status }: { status: StrategyStatus }) {
  return <Badge label={STATUS[status].label} tone={STATUS[status].tone} testID={`status-${status}`} />;
}

const tone = (value: string) => (new Decimal(value).isNegative() ? 'loss' : 'gain') as 'loss' | 'gain';

/** CAGR · Sharpe · Max DD · Win rate. */
export function HeadlineGrid({ metrics }: { metrics: HeadlineMetrics }) {
  return (
    <MetricGrid
      items={[
        { label: 'CAGR', value: formatPct(metrics.cagr, { dp: 1 }), tone: tone(metrics.cagr) },
        { label: 'Sharpe', value: formatRatio(metrics.sharpe) },
        { label: 'Max DD', value: formatPct(metrics.maxDrawdown, { dp: 1 }), tone: 'loss' },
        { label: 'Win rate', value: formatPct(metrics.winRate, { dp: 0 }) },
      ]}
    />
  );
}

/** "30-day drawdown −5.4% · pauses at −6.0%" (design 17), shown while the guard is warning or has paused the strategy. */
export function GuardLine({ strategy }: { strategy: StrategySummary }) {
  const g = strategy.guard;
  if (!g || strategy.status === 'active') return null;
  const text =
    strategy.status === 'paused'
      ? `Paused by the drawdown guard · ${g.windowDays}-day drawdown ${formatPct(g.drawdown, { dp: 1 })}. No new signals.`
      : `${g.windowDays}-day drawdown ${formatPct(g.drawdown, { dp: 1 })} · pauses at ${formatPct(g.pauseAt, { dp: 1 })}`;
  return (
    <Text.DataSm tone={strategy.status === 'paused' ? 'loss' : 'pending'} testID="guard-line">
      {text}
    </Text.DataSm>
  );
}

/** Strategy lab card (design 13). */
export function StrategyCard({ strategy, onPress }: { strategy: StrategySummary; onPress: () => void }) {
  const s = strategy;
  return (
    <Pressable onPress={onPress} accessibilityRole="button" accessibilityLabel={`${s.name}, ${STATUS[s.status].label}`} style={({ pressed }) => [styles.card, { opacity: pressed ? 0.8 : 1 }]} testID={`strategy-${s.code}`}>
      <View style={styles.cardHead}>
        <View style={styles.flex}>
          <Text.Section>{s.name}</Text.Section>
          <Text.Caption tone="secondary" numberOfLines={2}>
            {s.description}
          </Text.Caption>
        </View>
        <StrategyStatusBadge status={s.status} />
      </View>
      {s.sparkline.length > 1 ? <Sparkline closes={s.sparkline} width={280} height={44} /> : null}
      {s.metrics ? <HeadlineGrid metrics={s.metrics} /> : <Text.Caption tone="tertiary">No backtest yet.</Text.Caption>}
      <GuardLine strategy={s} />
      {s.liveSince ? (
        <Text.DataSm tone="tertiary">
          {`Live since ${formatMonthYear(s.liveSince)}`}
          {s.live ? (
            <Text.DataSm tone={tone(s.live.mtdPct)}>{` · MTD ${formatPct(s.live.mtdPct, { signed: true, dp: 1 })}`}</Text.DataSm>
          ) : null}
        </Text.DataSm>
      ) : null}
    </Pressable>
  );
}

export interface MetricsTableColumn {
  key: string;
  name: string;
  color?: string;
  metrics: Partial<FullMetrics> & HeadlineMetrics;
}

type Row = { label: string; value: (m: MetricsTableColumn['metrics']) => string | null };

const ROWS: readonly Row[] = [
  { label: 'CAGR', value: (m) => formatPct(m.cagr, { dp: 1 }) },
  { label: 'Sharpe', value: (m) => formatRatio(m.sharpe) },
  { label: 'Max DD', value: (m) => formatPct(m.maxDrawdown, { dp: 1 }) },
  { label: 'Win rate', value: (m) => formatPct(m.winRate, { dp: 0 }) },
  { label: 'Profit factor', value: (m) => (m.profitFactor ? formatRatio(m.profitFactor) : null) },
  { label: 'Trades', value: (m) => (m.trades !== undefined ? String(m.trades) : null) },
  { label: 'Growth of $1', value: (m) => (m.multiple ? `${formatRatio(m.multiple)}×` : null) },
];

/** Side-by-side metrics (design 14 compare; design 11 IS vs OOS). Rows with no value in any column are dropped. */
export function MetricsTable({ columns, testID }: { columns: readonly MetricsTableColumn[]; testID?: string }) {
  const rows = ROWS.filter((r) => columns.some((c) => r.value(c.metrics) !== null));
  return (
    <View style={styles.table} testID={testID}>
      <View style={styles.tableRow}>
        <View style={styles.labelCell} />
        {columns.map((c) => (
          <View key={c.key} style={styles.valueCell}>
            {c.color ? <View style={[styles.swatch, { backgroundColor: c.color }]} /> : null}
            <Text.Caption tone="secondary" numberOfLines={2} style={styles.right}>
              {c.name}
            </Text.Caption>
          </View>
        ))}
      </View>
      {rows.map((r) => (
        <View key={r.label} style={[styles.tableRow, styles.divider]}>
          <Text.Caption tone="tertiary" style={styles.labelCell}>
            {r.label}
          </Text.Caption>
          {columns.map((c) => (
            <View key={c.key} style={styles.valueCell} accessible accessibilityLabel={`${c.name} ${r.label}: ${r.value(c.metrics) ?? 'not available'}`}>
              <Text.DataSm style={styles.right}>{r.value(c.metrics) ?? '—'}</Text.DataSm>
            </View>
          ))}
        </View>
      ))}
    </View>
  );
}

/** Monthly returns heatmap (design 11): one row per year, cells shaded by sign and size, capped at ±10 %. */
export function MonthlyHeatmap({ returns }: { returns: readonly (readonly [string, string])[] }) {
  const byYear = new Map<string, (string | null)[]>();
  for (const [month, value] of returns) {
    const [year, mm] = month.split('-') as [string, string];
    const row = byYear.get(year) ?? Array<string | null>(12).fill(null);
    row[Number(mm) - 1] = value;
    byYear.set(year, row);
  }
  const years = [...byYear.keys()].sort();
  return (
    <View style={styles.heatmap} testID="monthly-heatmap">
      <View style={styles.heatRow}>
        <View style={styles.yearCell} />
        {MONTHS.map((m) => (
          <Text.Caption key={m} tone="tertiary" style={styles.heatHeader}>
            {m[0]}
          </Text.Caption>
        ))}
      </View>
      {years.map((year) => (
        <View key={year} style={styles.heatRow}>
          <Text.Caption tone="tertiary" style={styles.yearCell}>
            {year}
          </Text.Caption>
          {byYear.get(year)!.map((value, i) => (
            <View
              key={i}
              accessible={value !== null}
              accessibilityLabel={value !== null ? `${MONTHS[i]} ${year}: ${formatPct(value, { signed: true, dp: 1 })}` : undefined}
              style={[styles.heatCell, value !== null ? { backgroundColor: heatColor(value) } : null]}
            />
          ))}
        </View>
      ))}
    </View>
  );
}

function heatColor(value: string): string {
  // Opacity only: the value itself never feeds money math.
  const intensity = Math.min(1, Math.abs(Number(value)) / 0.1);
  const alpha = (0.15 + intensity * 0.75).toFixed(2);
  return new Decimal(value).isNegative() ? `rgba(255,107,107,${alpha})` : `rgba(61,214,140,${alpha})`;
}

const styles = StyleSheet.create({
  card: { gap: space[3], padding: space[4], borderRadius: radius.card, backgroundColor: color.surface1, borderWidth: 1, borderColor: color.line },
  cardHead: { flexDirection: 'row', gap: space[3], alignItems: 'flex-start' },
  flex: { flex: 1, gap: 2 },
  table: { gap: 0 },
  tableRow: { flexDirection: 'row', alignItems: 'center', minHeight: 36, gap: space[2] },
  divider: { borderTopWidth: 1, borderTopColor: color.line },
  labelCell: { width: 96 },
  valueCell: { flex: 1, alignItems: 'flex-end', gap: 2 },
  right: { textAlign: 'right' },
  swatch: { width: 12, height: 3, borderRadius: 2 },
  heatmap: { gap: 3 },
  heatRow: { flexDirection: 'row', alignItems: 'center', gap: 3 },
  yearCell: { width: 36 },
  heatHeader: { flex: 1, textAlign: 'center' },
  heatCell: { flex: 1, aspectRatio: 1, borderRadius: 3, backgroundColor: color.surface2 },
});
