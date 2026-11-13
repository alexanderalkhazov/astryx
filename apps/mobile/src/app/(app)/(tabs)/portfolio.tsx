import type { PerformanceRange } from '@astryx/api-client';
import { space } from '@astryx/design-tokens';
import { useQuery } from '@tanstack/react-query';
import Decimal from 'decimal.js-light';
import { router } from 'expo-router';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';

import { LineChart } from '@/components/chart/LineChart';
import { EmptyState, SkeletonRow } from '@/components/Feedback';
import { MetricGrid, MetricHero } from '@/components/Metric';
import { Segmented } from '@/components/Segmented';
import { PaperChip, Screen, ScreenHeader } from '@/components/Shell';
import { StatusBanner } from '@/components/StatusBanner';
import { Tabs } from '@/components/Tabs';
import { Text } from '@/components/Text';
import { AssetDonut, PositionRow, SectorAllocation } from '@/features/portfolio/PortfolioParts';
import { portfolio } from '@/lib/api';
import { formatEtTime, formatMoney, formatPct, formatPnl, formatRatio } from '@/lib/format';

type Tab = 'positions' | 'allocation' | 'performance';
const RANGES: readonly PerformanceRange[] = ['1W', '1M', '3M', '1Y', 'ALL'];

const tone = (value: string) => (new Decimal(value).isNegative() ? 'loss' : 'gain') as 'loss' | 'gain';

// Designs 09 (Positions) and 15 (Allocation) · SYSTEM_DESIGN.md §8.4.6. Every value is a server valuation.
export default function PortfolioScreen() {
  const [tab, setTab] = useState<Tab>('positions');
  const summary = useQuery({ queryKey: ['portfolio', 'summary'], queryFn: async () => (await portfolio.summary()).data });

  const header = <ScreenHeader title="Portfolio" chip={<PaperChip />} />;
  if (summary.isError) {
    return (
      <Screen header={header}>
        <EmptyState title="Couldn't load your portfolio" body="Check your connection and try again." action={{ label: 'Retry', onPress: () => void summary.refetch() }} />
      </Screen>
    );
  }

  const s = summary.data;
  return (
    <Screen header={header}>
      {s?.stale ? <StatusBanner tone="error" title="Prices may be out of date" body={`Valued at quotes from ${formatEtTime(s.asOf)}. Values refresh when the feed recovers.`} /> : null}
      {s ? (
        <View style={styles.hero}>
          <MetricHero label="Portfolio value" value={s.equity}>
            <View style={styles.heroLine}>
              <Text.DataSm tone={tone(s.dayPnl)}>{`${formatPnl(s.dayPnl)}${s.dayPnlPct ? ` (${formatPct(s.dayPnlPct, { signed: true })})` : ''} today`}</Text.DataSm>
              <Text.DataSm tone={tone(s.allTimePnl)}>{`${formatPnl(s.allTimePnl)} (${formatPct(s.allTimeReturnPct, { signed: true })}) all time`}</Text.DataSm>
            </View>
          </MetricHero>
          {s.halted ? <StatusBanner tone="error" title="New entries paused" body="The daily loss halt is reached. Entries resume next session." /> : null}
        </View>
      ) : (
        <SkeletonRow />
      )}

      <Tabs<Tab>
        tabs={[
          { value: 'positions', label: 'Positions' },
          { value: 'allocation', label: 'Allocation' },
          { value: 'performance', label: 'Performance' },
        ]}
        value={tab}
        onChange={setTab}
      />
      {tab === 'positions' ? <PositionsTab /> : tab === 'allocation' ? <AllocationTab /> : <PerformanceTab />}
    </Screen>
  );
}

function PositionsTab() {
  const summary = useQuery({ queryKey: ['portfolio', 'summary'], queryFn: async () => (await portfolio.summary()).data });
  const positions = useQuery({ queryKey: ['positions', 'open'], queryFn: async () => (await portfolio.positions('open')).data });
  if (positions.isPending) return <SkeletonRow />;
  const items = positions.data?.items ?? [];
  return (
    <View style={styles.tab} testID="positions-tab">
      {summary.data ? (
        <MetricGrid
          items={[
            { label: 'Invested', value: formatMoney(summary.data.invested) },
            { label: 'Unrealized', value: formatPnl(summary.data.unrealizedPnl), tone: tone(summary.data.unrealizedPnl) },
            { label: 'Cash', value: formatMoney(summary.data.cash) },
          ]}
        />
      ) : null}
      {items.length === 0 ? (
        <EmptyState title="No open positions" body="Approved proposals that fill appear here." action={{ label: 'Find a setup', onPress: () => router.push('/discover') }} />
      ) : (
        <View>
          <View style={styles.listHeader}>
            <Text.Overline tone="tertiary">SYMBOL · WEIGHT</Text.Overline>
            <Text.Overline tone="tertiary">VALUE · P&L</Text.Overline>
          </View>
          {items.map((p) => (
            <PositionRow key={p.id} position={p} onPress={() => router.push({ pathname: '/position/[ticker]', params: { ticker: p.symbol } })} />
          ))}
        </View>
      )}
    </View>
  );
}

function AllocationTab() {
  const allocation = useQuery({ queryKey: ['portfolio', 'allocation'], queryFn: async () => (await portfolio.allocation()).data });
  const exposure = useQuery({ queryKey: ['portfolio', 'exposure'], queryFn: async () => (await portfolio.exposure()).data });
  if (!allocation.data || !exposure.data) return <SkeletonRow />;
  const e = exposure.data;
  return (
    <View style={styles.tab} testID="allocation-tab">
      <Text.Overline tone="tertiary">Sectors</Text.Overline>
      {allocation.data.sectors.length ? <SectorAllocation allocation={allocation.data} /> : <Text.Body tone="secondary">No open positions, so everything is in cash.</Text.Body>}
      <Text.Overline tone="tertiary">Assets</Text.Overline>
      <AssetDonut assets={allocation.data.assets} />
      <Text.Overline tone="tertiary">Exposure</Text.Overline>
      <MetricGrid
        items={[
          { label: 'Gross exposure', value: formatPct(e.grossPct, { dp: 1 }) },
          { label: 'Beta-weighted', value: e.betaWeightedPct ? formatPct(e.betaWeightedPct, { dp: 1 }) : '—' },
          { label: 'Open risk to stops', value: formatMoney(e.openRiskToStops) },
        ]}
      />
      {e.betaWeightedPct === null ? <Text.Caption tone="tertiary">Beta-weighted exposure needs a beta for every holding, which the data source doesn't provide for all.</Text.Caption> : null}
      {e.concentrationWarnings.map((w) => (
        <StatusBanner key={`${w.kind}-${w.subject}`} tone="error" title="Concentration" body={w.message} />
      ))}
    </View>
  );
}

function PerformanceTab() {
  const [range, setRange] = useState<PerformanceRange>('1M');
  const performance = useQuery({ queryKey: ['portfolio', 'performance', range], queryFn: async () => (await portfolio.performance(range)).data });
  const trades = useQuery({ queryKey: ['portfolio', 'trades'], queryFn: async () => (await portfolio.trades()).data });
  const p = performance.data;
  return (
    <View style={styles.tab} testID="performance-tab">
      <Segmented accessibilityLabel="Performance range" options={RANGES.map((r) => ({ value: r, label: r }))} value={range} onChange={setRange} />
      {p ? (
        <>
          <LineChart
            series={[{ key: 'equity', points: p.series.map((x) => ({ t: x.t, v: x.equity })) }]}
            benchmark={p.series.every((x) => x.benchmark) ? p.series.map((x) => ({ t: x.t, v: x.benchmark! })) : undefined}
            accessibilityLabel={`Equity over ${range}${p.metrics.twrPct ? `, ${formatPct(p.metrics.twrPct, { signed: true })}` : ''}, against ${p.benchmark}`}
          />
          <MetricGrid
            items={[
              { label: 'Return (TWR)', value: p.metrics.twrPct ? formatPct(p.metrics.twrPct, { signed: true }) : '—' },
              { label: `vs ${p.benchmark}`, value: p.metrics.benchmarkPct ? formatPct(p.metrics.benchmarkPct, { signed: true }) : '—' },
              { label: 'Max drawdown', value: formatPct(p.metrics.maxDrawdownPct), tone: 'loss' },
            ]}
          />
          <MetricGrid
            items={[
              { label: 'Win rate', value: p.metrics.winRate ? formatPct(p.metrics.winRate, { dp: 0 }) : '—' },
              { label: 'Avg win', value: p.metrics.avgWin ? formatPnl(p.metrics.avgWin) : '—', tone: 'gain' },
              { label: 'Avg loss', value: p.metrics.avgLoss ? formatPnl(p.metrics.avgLoss) : '—', tone: 'loss' },
            ]}
          />
        </>
      ) : (
        <SkeletonRow />
      )}
      <Text.Overline tone="tertiary">Closed trades</Text.Overline>
      {(trades.data?.items ?? []).length === 0 ? (
        <Text.Body tone="secondary">No closed trades yet.</Text.Body>
      ) : (
        trades.data!.items.map((t) => (
          <View key={t.positionId} style={styles.trade} accessible accessibilityLabel={`${t.symbol} ${t.exitReason}, ${formatPnl(t.realizedPnl)}`}>
            <View style={styles.flex}>
              <Text.Section>{t.symbol}</Text.Section>
              <Text.Caption tone="secondary">{`${t.exitReason === 'stop' ? 'Stop' : t.exitReason === 'target' ? 'Target' : 'Closed'} · ${t.holdingDays} d · ${formatRatio(t.quantity, 0)} sh`}</Text.Caption>
            </View>
            <View style={styles.right}>
              <Text.Data tone={tone(t.realizedPnl)}>{formatPnl(t.realizedPnl)}</Text.Data>
              <Text.DataSm tone="tertiary">{formatPct(t.returnPct, { signed: true })}</Text.DataSm>
            </View>
          </View>
        ))
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  hero: { gap: space[3] },
  heroLine: { gap: 2 },
  tab: { gap: space[4], paddingTop: space[3] },
  listHeader: { flexDirection: 'row', justifyContent: 'space-between', paddingBottom: space[2] },
  trade: { flexDirection: 'row', alignItems: 'center', gap: space[3], minHeight: 48 },
  flex: { flex: 1 },
  right: { alignItems: 'flex-end' },
});
