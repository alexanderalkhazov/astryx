import type { Fundamentals } from '@astryx/api-client';
import { color, space } from '@astryx/design-tokens';
import { StyleSheet, View } from 'react-native';

import { EmptyState, SkeletonRow } from '@/components/Feedback';
import { Text } from '@/components/Text';
import { formatCompactMoney, formatEtTime, formatPrice, formatRatio, formatSessionDate } from '@/lib/format';

import { useFundamentals } from './useStock';

const MISSING = '—';

/** Fundamentals tab (design 16, SYSTEM_DESIGN.md §8.4.3). Missing values show "—"; sector medians arrive with the scanner. */
export function FundamentalsTab({ symbol }: { symbol: string }) {
  const fundamentals = useFundamentals(symbol, true);
  if (fundamentals.isPending) return <SkeletonRow />;
  if (fundamentals.isError || !fundamentals.data) {
    return <EmptyState title="Fundamentals unavailable" body="The data source didn't answer. Try again shortly." action={{ label: 'Retry', onPress: () => void fundamentals.refetch() }} />;
  }

  const f: Fundamentals = fundamentals.data;
  const rows: [string, string | null][] = [
    ['Market cap', f.marketCap ? formatCompactMoney(f.marketCap) : null],
    ['P/E (TTM)', f.peRatio ? formatRatio(f.peRatio, 1) : null],
    ['EPS (TTM)', f.epsTtm ? formatPrice(f.epsTtm) : null],
    ['Beta', f.beta ? formatRatio(f.beta) : null],
    ['Next earnings', f.nextEarningsDate ? formatSessionDate(f.nextEarningsDate) : null],
    ['Sector', f.sector],
    ['Industry', f.industry],
  ];
  const anyMissing = rows.some(([, v]) => v === null);

  return (
    <View testID="fundamentals-tab">
      {rows.map(([label, value], index) => (
        <View key={label} style={[styles.row, index > 0 && styles.divider]} accessible accessibilityLabel={`${label}, ${value ?? 'not provided'}`}>
          <Text.Body tone="secondary">{label}</Text.Body>
          <Text.Data tone={value === null ? 'tertiary' : 'primary'}>{value ?? MISSING}</Text.Data>
        </View>
      ))}
      <Text.Caption tone="tertiary" style={styles.note}>
        {[anyMissing ? '— Not provided by data source' : null, f.asOf ? `As of ${formatEtTime(f.asOf)}` : null, 'Sector comparison arrives with the scanner'].filter(Boolean).join(' · ')}
      </Text.Caption>
    </View>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', minHeight: 44, gap: space[3] },
  divider: { borderTopWidth: 1, borderTopColor: color.line },
  note: { marginTop: space[3] },
});
