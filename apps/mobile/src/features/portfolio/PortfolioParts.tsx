import type { Allocation, PortfolioPosition } from '@astryx/api-client';
import { color, radius, space } from '@astryx/design-tokens';
import { Canvas, Path, Skia } from '@shopify/react-native-skia';
import Decimal from 'decimal.js-light';
import { Pressable, StyleSheet, View } from 'react-native';

import { Badge } from '@/components/Badge';
import { Text } from '@/components/Text';
import { formatMoney, formatPct, formatPnl, formatPrice, formatQuantity } from '@/lib/format';

const share = (value: string, of: string) => Math.max(0, Math.min(1, new Decimal(value).div(new Decimal(of).isZero() ? 1 : of).toNumber())); // layout only

/** Weight against the cap (design 09): the fill is amber once the holding has drifted over the cap. */
export function WeightBar({ weight, cap, over }: { weight: string; cap: string; over: boolean }) {
  // The track spans 1.5 × the cap so drift past it stays visible.
  const span = new Decimal(cap).times(1.5).toString();
  return (
    <View style={styles.weightTrack} accessible accessibilityLabel={`Weight ${formatPct(weight)} of a ${formatPct(cap, { dp: 0 })} cap${over ? ', over the cap' : ''}`}>
      <View style={[styles.weightFill, { width: `${share(weight, span) * 100}%`, backgroundColor: over ? color.pending : color.signal }]} />
      <View style={[styles.capMark, { left: `${(2 / 3) * 100}%` }]} />
    </View>
  );
}

/** PositionRow (design 09): symbol, NEW, shares · avg cost, value, P&L, weight bar and the stop-proximity badge. */
export function PositionRow({ position, onPress }: { position: PortfolioPosition; onPress: () => void }) {
  const p = position;
  return (
    <Pressable onPress={onPress} accessibilityRole="button" accessibilityLabel={`${p.symbol}, ${formatQuantity(p.quantity)} shares, value ${p.marketValue ? formatMoney(p.marketValue) : 'unknown'}`} style={({ pressed }) => [styles.row, { opacity: pressed ? 0.7 : 1 }]} testID={`position-${p.symbol}`}>
      <View style={styles.rowTop}>
        <View style={styles.left}>
          <View style={styles.symbolLine}>
            <Text.Section>{p.symbol}</Text.Section>
            {p.isNew ? <Badge label="New" tone="signal" mono /> : null}
            {p.nearStop ? <Badge label={`${formatPct(p.stopDistancePct ?? '0', { dp: 1 })} to stop`} tone="loss" mono /> : null}
          </View>
          <Text.Caption tone="secondary">{`${formatQuantity(p.quantity)} sh · avg ${formatPrice(p.avgEntryPrice)}`}</Text.Caption>
        </View>
        <View style={styles.right}>
          <Text.Data>{p.marketValue ? formatMoney(p.marketValue) : '—'}</Text.Data>
          {p.unrealizedPnl ? (
            <Text.DataSm tone={new Decimal(p.unrealizedPnl).isNegative() ? 'loss' : 'gain'}>{`${formatPnl(p.unrealizedPnl)} (${formatPct(p.unrealizedPct ?? '0', { signed: true })})`}</Text.DataSm>
          ) : null}
        </View>
      </View>
      {p.weightPct ? (
        <View style={styles.weightLine}>
          <WeightBar weight={p.weightPct} cap={p.weightCapPct} over={p.overCap} />
          <Text.DataSm tone={p.overCap ? 'pending' : 'tertiary'}>{`${formatPct(p.weightPct, { dp: 1 })} / ${formatPct(p.weightCapPct, { dp: 0 })}`}</Text.DataSm>
        </View>
      ) : null}
    </Pressable>
  );
}

const SECTOR_COLORS = color.series;

/** Sector stacked bar plus rows (design 15). Rows over the warning line say how near the limit they are. */
export function SectorAllocation({ allocation }: { allocation: Allocation }) {
  return (
    <View style={styles.gap}>
      <View style={styles.stacked} accessible accessibilityLabel={allocation.sectors.map((s) => `${s.sector} ${formatPct(s.pct, { dp: 1 })}`).join(', ')}>
        {allocation.sectors.map((s, i) => (
          <View key={s.sector} style={{ flex: Math.max(share(s.pct, '1'), 0.0001), backgroundColor: SECTOR_COLORS[i % SECTOR_COLORS.length] }} />
        ))}
      </View>
      {allocation.sectors.map((s, i) => (
        <View key={s.sector} style={styles.sectorRow} accessible accessibilityLabel={`${s.sector}: ${formatPct(s.pct, { dp: 1 })}, ${formatMoney(s.value)}${s.warning ? `, near the ${formatPct(allocation.maxSectorExposurePct, { dp: 0 })} limit` : ''}`}>
          <View style={[styles.dot, { backgroundColor: SECTOR_COLORS[i % SECTOR_COLORS.length] }]} />
          <Text.Body style={styles.flex}>{s.sector}</Text.Body>
          {s.warning ? <Badge label={s.overLimit ? 'Over limit' : `Near limit ${formatPct(allocation.maxSectorExposurePct, { dp: 0 })}`} tone="pending" /> : null}
          <Text.Data>{formatPct(s.pct, { dp: 1 })}</Text.Data>
          <Text.DataSm tone="tertiary">{formatMoney(s.value)}</Text.DataSm>
        </View>
      ))}
    </View>
  );
}

/** Asset allocation donut (design 15): stocks vs cash. Drawn from server percentages; the arcs are layout only. */
export function AssetDonut({ assets, size = 120 }: { assets: Allocation['assets']; size?: number }) {
  const stroke = 16;
  const r = (size - stroke) / 2;
  let start = -90;
  const arcs = assets.map((a, i) => {
    const sweep = share(a.pct, '1') * 360;
    const path = Skia.PathBuilder.Make().addArc({ x: stroke / 2, y: stroke / 2, width: r * 2, height: r * 2 }, start, Math.max(sweep - 1, 0)).build();
    start += sweep;
    return { key: a.asset, path, color: a.asset === 'cash' ? color.lineStrong : SECTOR_COLORS[i % SECTOR_COLORS.length] };
  });
  return (
    <View style={styles.donutRow} accessible accessibilityLabel={assets.map((a) => `${a.asset === 'cash' ? 'Cash' : 'US equities'} ${formatPct(a.pct, { dp: 1 })}`).join(', ')} testID="asset-donut">
      <Canvas style={{ width: size, height: size }}>
        {arcs.map((a) => (
          <Path key={a.key} path={a.path} color={a.color} style="stroke" strokeWidth={stroke} />
        ))}
      </Canvas>
      <View style={styles.gap}>
        {assets.map((a) => (
          <View key={a.asset} style={styles.sectorRow}>
            <Text.Body style={styles.flex}>{a.asset === 'cash' ? 'Cash' : 'US equities'}</Text.Body>
            <Text.Data>{formatPct(a.pct, { dp: 1 })}</Text.Data>
          </View>
        ))}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  gap: { gap: space[2] },
  flex: { flex: 1 },
  row: { gap: space[2], paddingVertical: space[3], borderBottomWidth: 1, borderBottomColor: color.line },
  rowTop: { flexDirection: 'row', justifyContent: 'space-between', gap: space[3] },
  left: { flex: 1, gap: 2 },
  right: { alignItems: 'flex-end', gap: 2 },
  symbolLine: { flexDirection: 'row', alignItems: 'center', gap: space[2], flexWrap: 'wrap' },
  weightLine: { flexDirection: 'row', alignItems: 'center', gap: space[3] },
  weightTrack: { flex: 1, height: 6, borderRadius: radius.chip, backgroundColor: color.surface2, overflow: 'hidden' },
  weightFill: { position: 'absolute', left: 0, top: 0, bottom: 0, borderRadius: radius.chip },
  capMark: { position: 'absolute', top: -2, bottom: -2, width: 2, backgroundColor: color.text3 },
  stacked: { flexDirection: 'row', height: 12, borderRadius: radius.chip, overflow: 'hidden', gap: 2 },
  sectorRow: { flexDirection: 'row', alignItems: 'center', gap: space[2], minHeight: 32 },
  dot: { width: 8, height: 8, borderRadius: 4 },
  donutRow: { flexDirection: 'row', alignItems: 'center', gap: space[5] },
});
