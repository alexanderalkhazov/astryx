import type { Signal } from '@astryx/api-client';
import { color, radius, space } from '@astryx/design-tokens';
import { Pressable, StyleSheet, View } from 'react-native';

import { SignalBadge } from './Badge';
import { Sparkline } from './chart/Sparkline';
import { Text } from './Text';
import { changeDirection, formatPct, formatPrice, formatRatio, formatVolume } from '@/lib/format';

/** Scores at or above this read as strong (blue), below as neutral (design 02). */
const STRONG_SCORE = 80;

/**
 * One ranked setup (design 02 · OpportunityRow, SYSTEM_DESIGN.md §8.6): rank, ticker with signal badge, company ·
 * industry, sparkline, price and day change, the deterministic reason, then score (with bar), volume, relative
 * strength, R:R and RSI.
 */
export function OpportunityRow({ signal, onPress }: { signal: Signal; onPress: () => void }) {
  const score = Number(signal.score); // display only
  const strong = score >= STRONG_SCORE;
  const direction = signal.changePct === null ? null : changeDirection(signal.changePct);
  const rsi = signal.metrics.rsi14 === null ? null : formatRatio(signal.metrics.rsi14, 0);
  const label = [
    `Rank ${signal.rank}`,
    signal.symbol,
    signal.name,
    `${signal.type.replace('_', ' ')} signal`,
    signal.price ? formatPrice(signal.price) : null,
    signal.changePct && direction ? `${direction} ${formatPct(signal.changePct.replace('-', ''))}` : null,
    signal.reason,
    `score ${signal.score}`,
  ]
    .filter(Boolean)
    .join(', ');

  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityHint="Opens the stock"
      onPress={onPress}
      testID={`opportunity-${signal.symbol}`}
      style={({ pressed }) => [styles.row, { opacity: pressed ? 0.7 : 1 }]}
    >
      <Text.DataSm tone="tertiary" style={styles.rank}>
        {String(signal.rank).padStart(2, '0')}
      </Text.DataSm>
      <View style={styles.body}>
        <View style={styles.top}>
          <View style={styles.identity}>
            <View style={styles.tickerLine}>
              <Text.Data style={styles.ticker}>{signal.symbol}</Text.Data>
              <SignalBadge type={signal.type} />
            </View>
            <Text.Body tone="secondary" numberOfLines={1}>
              {[signal.name, signal.sector].filter(Boolean).join(' · ')}
            </Text.Body>
          </View>
          <Sparkline closes={signal.sparkline} width={72} height={36} />
          <View style={styles.price}>
            {signal.price ? <Text.Data>{formatPrice(signal.price)}</Text.Data> : null}
            {signal.changePct ? (
              <Text.DataSm tone={direction === 'up' ? 'gain' : direction === 'down' ? 'loss' : 'secondary'}>
                {formatPct(signal.changePct, { signed: true })}
              </Text.DataSm>
            ) : null}
          </View>
        </View>
        <Text.Body>{signal.reason}</Text.Body>
        <View style={styles.metrics}>
          <View style={styles.metric}>
            <Text.Overline tone="tertiary">Score</Text.Overline>
            <Text.Data tone={strong ? 'signal' : 'primary'}>{signal.score}</Text.Data>
            <View style={styles.track}>
              <View style={[styles.fill, { width: `${Math.min(score, 100)}%`, backgroundColor: strong ? color.signal : color.text }]} />
            </View>
          </View>
          <Metric
            label="Volume"
            value={signal.metrics.relVolume ? `${formatRatio(signal.metrics.relVolume, 1)}×` : '—'}
            caption={signal.volume ? formatVolume(signal.volume) : undefined}
          />
          <Metric label="Rel str" value={signal.metrics.relStrength ?? '—'} />
          <Metric label="R:R" value={signal.metrics.rewardRisk ? formatRatio(signal.metrics.rewardRisk, 1) : '—'} />
          <Metric label="RSI 14" value={rsi ?? '—'} />
        </View>
      </View>
    </Pressable>
  );
}

function Metric({ label, value, caption }: { label: string; value: string; caption?: string }) {
  return (
    <View style={styles.metric}>
      <Text.Overline tone="tertiary">{label}</Text.Overline>
      <Text.Data>{value}</Text.Data>
      {caption ? <Text.DataSm tone="tertiary">{caption}</Text.DataSm> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', gap: space[3], paddingVertical: space[4], borderBottomWidth: 1, borderBottomColor: color.line },
  rank: { width: 22, paddingTop: 2 },
  body: { flex: 1, gap: space[3] },
  top: { flexDirection: 'row', alignItems: 'center', gap: space[2] },
  identity: { flex: 1, gap: 2 },
  tickerLine: { flexDirection: 'row', alignItems: 'center', gap: space[2] },
  ticker: { fontFamily: 'GeistMono_600SemiBold', fontSize: 16 },
  price: { alignItems: 'flex-end', minWidth: 64, gap: 2 },
  metrics: { flexDirection: 'row', justifyContent: 'space-between' },
  metric: { gap: 2, minWidth: 48 },
  track: { height: 3, width: 56, borderRadius: radius.badge, backgroundColor: color.surface2, overflow: 'hidden', marginTop: 2 },
  fill: { height: 3, borderRadius: radius.badge },
});
