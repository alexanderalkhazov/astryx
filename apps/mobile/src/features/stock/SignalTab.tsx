import type { Signal } from '@astryx/api-client';
import { space } from '@astryx/design-tokens';
import { StyleSheet, View } from 'react-native';

import { EmptyState, SkeletonRow } from '@/components/Feedback';
import { MetricGrid, ScoreBreakdownBar } from '@/components/Metric';
import { Text } from '@/components/Text';
import { AnalysisCard } from '@/features/analysis/AnalysisCard';
import { formatEtTime, formatPct, formatPrice, formatRatio, formatSessionDate } from '@/lib/format';
import { previewDivide, previewSubtract } from '@/lib/decimal';

import { Bullet, Section } from './Sections';

const HEADLINES: Record<Signal['type'], string> = {
  breakout: 'Breaking out of its base',
  momentum: 'A momentum leader pushing higher',
  pullback: 'An orderly pullback in an uptrend',
  unusual_volume: 'Trading on unusual volume',
  earnings: 'Drifting higher after earnings',
};

const COMPONENTS: Record<string, string> = {
  setup: 'Setup quality',
  relativeStrength: 'Relative strength',
  volume: 'Volume confirmation',
  trend: 'Trend alignment',
  rewardRisk: 'Reward/risk',
};

/** "Nov 10 · 14:32 ET" for an instant. */
const when = (instant: string) => `${formatSessionDate(instant.slice(0, 10))} · ${formatEtTime(instant)}`;

/**
 * Stock › Signal tab (SYSTEM_DESIGN.md §8.4.3, T-068): headline, summary, levels, evidence, the deterministic score
 * breakdown and invalidation, all from the scanner's signal (template copy), then the on-demand AI analysis (T-075).
 */
export function SignalTab({
  symbol,
  signal,
  pending,
  unavailable,
  evaluatedAt,
}: {
  symbol: string;
  signal: Signal | null | undefined;
  pending: boolean;
  unavailable: boolean;
  evaluatedAt: string | null | undefined;
}) {
  if (unavailable) {
    return (
      <View style={styles.tab}>
        <EmptyState title="Signal unavailable" body="The scanner feed is unavailable, so signals are hidden rather than shown stale." />
        <AnalysisCard symbol={symbol} />
      </View>
    );
  }

  if (pending) return <SkeletonRow />;

  if (!signal) {
    return (
      <View style={styles.tab}>
        <EmptyState
          title={`No active signal${evaluatedAt ? ` · Evaluated ${formatEtTime(evaluatedAt)}` : ''}`}
          body="No strategy finds a setup in this stock right now. Star it to keep an eye on it."
        />
        <AnalysisCard symbol={symbol} />
      </View>
    );
  }

  const m = signal.metrics;
  const { entry, stop, target } = signal.levels;
  const stopPct = formatPct(previewSubtract(previewDivide(stop, entry), '1'), { signed: true });
  const targetPct = formatPct(previewSubtract(previewDivide(target, entry), '1'), { signed: true });
  const evidence = [
    m.relVolume ? `Volume ${formatRatio(m.relVolume, 1)}× its 20-day average` : null,
    m.relStrength ? `Relative strength ${m.relStrength} of 99 across the scanner universe` : null,
    m.rewardRisk ? `Reward:risk ${formatRatio(m.rewardRisk)} from entry ${formatPrice(entry)} to target ${formatPrice(target)}` : null,
    m.rsi14 ? `RSI(14) at ${formatRatio(m.rsi14, 1)}` : null,
  ].filter((e): e is string => e !== null);

  return (
    <View style={styles.tab} testID="signal-tab">
      <View style={styles.section}>
        <Text.Headline accessibilityRole="header">{HEADLINES[signal.type]}</Text.Headline>
        <Text.Body>{signal.reason}</Text.Body>
      </View>

      <MetricGrid
        items={[
          { label: signal.levels.orderType === 'buy_stop' ? 'Buy stop' : 'Entry', value: formatPrice(entry) },
          { label: `Stop ${stopPct}`, value: formatPrice(stop), tone: 'loss' },
          { label: `Target ${targetPct}`, value: formatPrice(target), tone: 'gain' },
        ]}
      />

      <Section title="Evidence">
        {evidence.map((e) => (
          <Bullet key={e} text={e} />
        ))}
      </Section>

      <Section title={`Score ${signal.score} · how it adds up`}>
        {(signal.scoreBreakdown ?? []).map((c) => (
          <ScoreBreakdownBar
            key={c.component}
            label={COMPONENTS[c.component] ?? c.component}
            value={formatRatio(c.contribution, 1)}
            max={formatRatio(c.weight, 0)}
          />
        ))}
      </Section>

      <Section title="What would invalidate it">
        <Bullet text={`A close below the stop at ${formatPrice(stop)}.`} />
        <Bullet
          text={`The ${signal.levels.orderType === 'buy_stop' ? 'buy stop' : 'entry'} at ${formatPrice(entry)} lapses if it isn't triggered by ${when(signal.expiresAt)}, unless a newer scan confirms it.`}
        />
        <Bullet text="A newer scan that no longer finds the setup retires the signal." />
      </Section>

      <Text.Caption tone="tertiary">
        {`Deterministic: ${signal.strategy.name} rules, evaluated ${when(signal.evaluatedAt)}. Scores come from strategy rules, not AI. Research only, not investment advice.`}
      </Text.Caption>

      <AnalysisCard symbol={symbol} />
    </View>
  );
}

const styles = StyleSheet.create({
  tab: { gap: space[5], paddingTop: space[3] },
  section: { gap: space[2] },
});
