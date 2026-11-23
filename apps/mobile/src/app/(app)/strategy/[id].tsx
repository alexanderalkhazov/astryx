import { isApiError, type BacktestRun, type StrategyDetail } from '@astryx/api-client';
import { color, space } from '@astryx/design-tokens';
import { useQuery } from '@tanstack/react-query';
import Decimal from 'decimal.js-light';
import { router, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';

import { Button } from '@/components/Button';
import { LineChart } from '@/components/chart/LineChart';
import { EmptyState, SkeletonRow } from '@/components/Feedback';
import { MetricGrid } from '@/components/Metric';
import { StackHeader } from '@/components/Navigation';
import { Screen } from '@/components/Shell';
import { StatusBanner } from '@/components/StatusBanner';
import { Stepper } from '@/components/Stepper';
import { Tabs } from '@/components/Tabs';
import { Text } from '@/components/Text';
import { INTRO } from '@/features/plain/screenIntros';
import {
  formatMonthYear,
  GuardLine,
  ILLUSTRATIVE_COPY,
  MetricsTable,
  MonthlyHeatmap,
  StrategyStatusBadge,
} from '@/features/strategies/StrategyParts';
import { strategies } from '@/lib/api';
import { formatEtTime, formatMoney, formatPct, formatPnl, formatPrice, formatRatio } from '@/lib/format';

type Tab = 'overview' | 'backtest' | 'configure';

const tone = (value: string) => (new Decimal(value).isNegative() ? 'loss' : 'gain') as 'loss' | 'gain';

// Designs 11 (Backtest) and 12 (Configure) · SYSTEM_DESIGN.md §8.4.9. `id` is the strategy id or its code.
export default function StrategyScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const [tab, setTab] = useState<Tab>('overview');
  const detail = useQuery({ queryKey: ['strategies', id], queryFn: async () => (await strategies.get(id)).data, retry: false });

  const d = detail.data;
  const header = <StackHeader title={d?.summary.name ?? 'Strategy'} subtitle={d ? `Version ${d.version.number}` : undefined} />;
  if (detail.isError) {
    const missing = isApiError(detail.error) && detail.error.status === 404;
    return (
      <Screen intro={INTRO.strategy} header={header}>
        {missing ? (
          <EmptyState
            title="Strategy not found"
            body="It may have been retired from the catalog."
            action={{ label: 'Strategy lab', onPress: () => router.replace('/strategies') }}
          />
        ) : (
          <EmptyState
            title="Couldn't load this strategy"
            body="Check your connection and try again."
            action={{ label: 'Retry', onPress: () => void detail.refetch() }}
          />
        )}
      </Screen>
    );
  }
  if (!d) {
    return (
      <Screen intro={INTRO.strategy} header={header}>
        <SkeletonRow />
      </Screen>
    );
  }

  return (
    <Screen intro={INTRO.strategy} header={header}>
      <View style={styles.head}>
        <Text.Body tone="secondary" style={styles.flex}>
          {d.summary.description}
        </Text.Body>
        <StrategyStatusBadge status={d.summary.status} />
      </View>
      <GuardLine strategy={d.summary} />
      <Tabs<Tab>
        tabs={[
          { value: 'overview', label: 'Overview' },
          { value: 'backtest', label: 'Backtest' },
          { value: 'configure', label: 'Configure' },
        ]}
        value={tab}
        onChange={setTab}
      />
      {tab === 'overview' ? <OverviewTab detail={d} /> : tab === 'backtest' ? <BacktestTab detail={d} /> : <ConfigureTab detail={d} />}
    </Screen>
  );
}

function OverviewTab({ detail }: { detail: StrategyDetail }) {
  const live = detail.summary.live;
  return (
    <View style={styles.tab} testID="overview-tab">
      <Text.Overline tone="tertiary">Rules</Text.Overline>
      {detail.rules.map((rule, i) => (
        <View key={i} style={styles.rule}>
          <Text.DataSm tone="tertiary">{`${i + 1}`}</Text.DataSm>
          <Text.Body style={styles.flex}>{rule}</Text.Body>
        </View>
      ))}

      <Text.Overline tone="tertiary">Live paper results</Text.Overline>
      {live ? (
        <>
          <MetricGrid
            items={[
              { label: 'Month to date', value: formatPct(live.mtdPct, { signed: true, dp: 1 }), tone: tone(live.mtdPct) },
              { label: 'MTD P&L', value: formatPnl(live.mtdPnl), tone: tone(live.mtdPnl) },
              {
                label: 'Since inception',
                value: formatPct(live.sinceInceptionPct, { signed: true, dp: 1 }),
                tone: tone(live.sinceInceptionPct),
              },
            ]}
          />
          <Text.Caption tone="tertiary">
            {`${detail.summary.liveSince ? `Live since ${formatMonthYear(detail.summary.liveSince)} · ` : ''}${live.mtdTrades} trades this month${live.illustrative ? ' · illustrative until your fills are attributed to strategies' : ''}`}
          </Text.Caption>
        </>
      ) : (
        <Text.Body tone="secondary">Not trading live yet.</Text.Body>
      )}

      <Text.Overline tone="tertiary">Today's signals</Text.Overline>
      {detail.todaysSignals.length === 0 ? (
        <Text.Body tone="secondary">No active signals from this strategy right now.</Text.Body>
      ) : (
        detail.todaysSignals.map((s) => (
          <Pressable
            key={s.id}
            accessibilityRole="button"
            accessibilityLabel={`${s.symbol}, entry ${formatPrice(s.entry)}, score ${formatRatio(s.score, 0)}`}
            onPress={() => router.push({ pathname: '/stock/[ticker]', params: { ticker: s.symbol } })}
            style={({ pressed }) => [styles.signal, { opacity: pressed ? 0.7 : 1 }]}
          >
            <View style={styles.flex}>
              <Text.Section>{s.symbol}</Text.Section>
              <Text.Caption tone="secondary">{`Evaluated ${formatEtTime(s.evaluatedAt)}`}</Text.Caption>
            </View>
            <View style={styles.right}>
              <Text.Data>{formatPrice(s.entry)}</Text.Data>
              <Text.DataSm tone="signal">{`Score ${formatRatio(s.score, 0)}`}</Text.DataSm>
            </View>
          </Pressable>
        ))
      )}
    </View>
  );
}

function BacktestTab({ detail }: { detail: StrategyDetail }) {
  const runId = detail.latestRun?.id;
  const run = useQuery({
    queryKey: ['strategies', detail.summary.id, 'runs', runId],
    queryFn: async () => (await strategies.run(detail.summary.id, runId)).data,
    enabled: !!runId,
  });
  if (!runId)
    return (
      <EmptyState
        title="No backtest yet"
        body="This strategy hasn't been tested on past prices yet. Check back after the next nightly run."
      />
    );
  if (run.isError)
    return (
      <EmptyState
        title="Couldn't load the backtest"
        body="Try again in a moment."
        action={{ label: 'Retry', onPress: () => void run.refetch() }}
      />
    );
  if (!run.data) return <SkeletonRow />;
  return <BacktestBody run={run.data} />;
}

function BacktestBody({ run }: { run: BacktestRun }) {
  const m = run.metrics.full;
  const oosIndex = run.equityCurve.findIndex(([d]) => d >= run.oosStart);
  const benchmark = run.assumptions.benchmark;
  return (
    <View style={styles.tab} testID="backtest-tab">
      <Text.DataSm tone="secondary">{`Run #${run.runNumber} · ${(run.finishedAt ?? run.periodEnd).slice(0, 10)} · v${run.version}`}</Text.DataSm>

      <View style={styles.legend}>
        <Text.Overline tone="tertiary">Equity</Text.Overline>
        <Text.Caption tone="tertiary">{`Shaded: out of sample from ${formatMonthYear(run.oosStart)}`}</Text.Caption>
      </View>
      <LineChart
        height={180}
        bandFrom={oosIndex >= 0 ? oosIndex : undefined}
        series={[{ key: 'equity', points: run.equityCurve.map(([t, v]) => ({ t, v })) }]}
        accessibilityLabel={`Backtest equity from ${formatMoney(run.initialCapital, { dp: 0 })} in ${formatMonthYear(run.periodStart)} to ${formatMoney(run.equityCurve[run.equityCurve.length - 1]?.[1] ?? run.initialCapital, { dp: 0 })} in ${formatMonthYear(run.periodEnd)}, out of sample from ${formatMonthYear(run.oosStart)}`}
      />
      <Text.Overline tone="tertiary">Drawdown</Text.Overline>
      <LineChart
        height={96}
        series={[{ key: 'drawdown', color: color.loss, points: run.drawdownCurve.map(([t, v]) => ({ t, v })) }]}
        accessibilityLabel={`Drawdown, deepest ${formatPct(m.maxDrawdown, { dp: 1 })}`}
      />

      <MetricGrid
        items={[
          { label: 'CAGR', value: formatPct(m.cagr, { dp: 1 }), tone: tone(m.cagr) },
          { label: 'Sharpe', value: formatRatio(m.sharpe) },
          { label: 'Max DD', value: formatPct(m.maxDrawdown, { dp: 1 }), tone: 'loss' },
          { label: 'Win rate', value: formatPct(m.winRate, { dp: 0 }) },
        ]}
      />
      <MetricGrid
        items={[
          { label: 'Profit factor', value: formatRatio(m.profitFactor) },
          { label: 'Avg trade', value: formatPct(m.avgTradePct, { signed: true }), tone: tone(m.avgTradePct) },
          { label: 'Trades', value: String(m.trades) },
          { label: 'Expectancy', value: `${formatRatio(m.expectancyR)}R` },
        ]}
      />

      <Text.Overline tone="tertiary">In sample vs out of sample</Text.Overline>
      <MetricsTable
        testID="is-oos-table"
        columns={[
          {
            key: 'is',
            name: `In sample · ${run.periodStart.slice(0, 4)}–${String(Number(run.oosStart.slice(0, 4)) - 1)}`,
            metrics: run.metrics.inSample,
          },
          {
            key: 'oos',
            name: `Out of sample · ${run.oosStart.slice(0, 4)}–${run.periodEnd.slice(0, 4)}`,
            metrics: run.metrics.outOfSample,
          },
        ]}
      />

      <Text.Overline tone="tertiary">Monthly returns</Text.Overline>
      <MonthlyHeatmap returns={run.monthlyReturns} />

      <Text.Overline tone="tertiary">Assumptions</Text.Overline>
      <Text.Caption tone="secondary">{`Fees ${run.assumptions.fees} · slippage ${run.assumptions.slippageBps} bps · ${run.assumptions.universe}`}</Text.Caption>
      {benchmark ? (
        <Text.Caption tone="secondary">{`Growth of $1: ${formatRatio(m.multiple)}× vs ${benchmark.name} ${formatRatio(benchmark.multiple)}×`}</Text.Caption>
      ) : null}
      <Text.Caption tone="pending">{run.assumptions.survivorship}</Text.Caption>
      <Text.Caption tone="tertiary">{ILLUSTRATIVE_COPY}</Text.Caption>
    </View>
  );
}

function ConfigureTab({ detail }: { detail: StrategyDetail }) {
  return (
    <View style={styles.tab} testID="configure-tab">
      <StatusBanner
        tone="rejected"
        title="Coming soon"
        body="Editing parameters and re-running backtests arrive in a later release. These are the published values."
      />
      {detail.version.parameters.map((p) => (
        <View key={p.key} style={styles.param}>
          <View style={styles.flex}>
            <Text.Section>{p.label}</Text.Section>
            <Text.Caption tone="tertiary">{`Default ${String(p.default ?? '—')}${p.unit ? ` ${p.unit}` : ''} · range ${String(p.min ?? '—')}–${String(p.max ?? '—')}`}</Text.Caption>
          </View>
          <Stepper
            value={String(p.value ?? p.default ?? '0')}
            min={p.min !== null ? String(p.min) : undefined}
            max={p.max !== null ? String(p.max) : undefined}
            step={p.step !== null ? String(p.step) : undefined}
            unit={p.unit}
            onChange={() => {}}
            disabled
          />
        </View>
      ))}
      <Button
        label={`Run backtest · ${detail.latestRun ? `${detail.latestRun.periodStart.slice(0, 4)}–${detail.latestRun.periodEnd.slice(0, 4)}` : '2015–2026'}`}
        disabled
      />
      <Text.Caption tone="tertiary">{`Version ${detail.version.number} · ${detail.version.state} · ${detail.version.codeVersion}`}</Text.Caption>
    </View>
  );
}

const styles = StyleSheet.create({
  head: { flexDirection: 'row', gap: space[3], alignItems: 'flex-start' },
  flex: { flex: 1 },
  tab: { gap: space[4], paddingTop: space[3] },
  rule: { flexDirection: 'row', gap: space[3] },
  signal: { flexDirection: 'row', alignItems: 'center', gap: space[3], minHeight: 56 },
  right: { alignItems: 'flex-end' },
  legend: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'baseline' },
  param: { flexDirection: 'row', alignItems: 'center', gap: space[3], minHeight: 64 },
});
