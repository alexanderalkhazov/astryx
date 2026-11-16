import { color, space } from '@astryx/design-tokens';
import { useQuery } from '@tanstack/react-query';
import { router } from 'expo-router';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';

import { Chip } from '@/components/Chip';
import { LineChart } from '@/components/chart/LineChart';
import { EmptyState, SkeletonRow } from '@/components/Feedback';
import { Segmented } from '@/components/Segmented';
import { PaperChip, Screen, ScreenHeader } from '@/components/Shell';
import { Text } from '@/components/Text';
import { formatMonthYear, ILLUSTRATIVE_COPY, MetricsTable, StrategyCard } from '@/features/strategies/StrategyParts';
import { strategies as strategiesApi } from '@/lib/api';
import { formatRatio } from '@/lib/format';

type View_ = 'strategies' | 'compare';

const useStrategies = () => useQuery({ queryKey: ['strategies'], queryFn: async () => (await strategiesApi.list()).data.items });

// Designs 13 (list) and 14 (compare) · SYSTEM_DESIGN.md §8.4.8. Read-only: configure and re-run are Phase 6+.
export default function StrategiesScreen() {
  const [view, setView] = useState<View_>('strategies');
  const list = useStrategies();

  const header = <ScreenHeader title="Strategy lab" chip={<PaperChip />} />;
  if (list.isError) {
    return (
      <Screen header={header}>
        <EmptyState
          title="Couldn't load strategies"
          body="Check your connection and try again."
          action={{ label: 'Retry', onPress: () => void list.refetch() }}
        />
      </Screen>
    );
  }

  const items = list.data ?? [];
  const start = items.find((s) => s.backtestStart)?.backtestStart;
  const end = items.find((s) => s.backtestEnd)?.backtestEnd;
  return (
    <Screen header={header}>
      <Segmented
        accessibilityLabel="Strategy lab view"
        mono={false}
        options={[
          { value: 'strategies', label: 'Strategies' },
          { value: 'compare', label: 'Compare' },
        ]}
        value={view}
        onChange={setView}
      />
      {list.isPending ? (
        <SkeletonRow />
      ) : items.length === 0 ? (
        <EmptyState title="No strategies" body="The strategy catalog is empty." />
      ) : (
        <>
          <Text.Caption tone="tertiary">
            {`${items.length} strategies${start && end ? ` · backtests ${formatMonthYear(start)} – ${formatMonthYear(end)}` : ''} · illustrative results`}
          </Text.Caption>
          {view === 'strategies' ? (
            <View style={styles.list}>
              {items.map((s) => (
                <StrategyCard key={s.id} strategy={s} onPress={() => router.push({ pathname: '/strategy/[id]', params: { id: s.code } })} />
              ))}
            </View>
          ) : (
            <CompareView ids={items.filter((s) => s.metrics).map((s) => ({ id: s.id, name: s.name }))} />
          )}
          <Text.Caption tone="tertiary">{ILLUSTRATIVE_COPY}</Text.Caption>
        </>
      )}
    </Screen>
  );
}

function CompareView({ ids }: { ids: readonly { id: string; name: string }[] }) {
  const [selected, setSelected] = useState<readonly string[]>(() => ids.slice(0, 5).map((s) => s.id));
  const compare = useQuery({
    queryKey: ['strategies', 'compare', selected],
    queryFn: async () => (await strategiesApi.compare(selected)).data,
    enabled: selected.length > 0,
  });
  const toggle = (id: string) =>
    setSelected((current) => (current.includes(id) ? current.filter((x) => x !== id) : [...current, id].slice(-5)));
  // Colours follow the catalog order so a strategy keeps its colour as others are toggled.
  const colorOf = (id: string) => color.series[ids.findIndex((s) => s.id === id) % color.series.length]!;

  return (
    <View style={styles.compare} testID="compare-view">
      <View style={styles.chips}>
        {ids.map((s) => (
          <Chip key={s.id} label={s.name} selected={selected.includes(s.id)} onPress={() => toggle(s.id)} />
        ))}
      </View>
      {selected.length === 0 ? (
        <Text.Body tone="secondary">Pick at least one strategy to compare.</Text.Body>
      ) : compare.isError ? (
        <EmptyState
          title="Couldn't load the comparison"
          body="Try again in a moment."
          action={{ label: 'Retry', onPress: () => void compare.refetch() }}
        />
      ) : !compare.data ? (
        <SkeletonRow />
      ) : (
        <>
          <Text.Overline tone="tertiary">Growth of $1</Text.Overline>
          <LineChart
            height={180}
            series={compare.data.series.map((s) => ({
              key: s.strategyId,
              color: colorOf(s.strategyId),
              points: s.points.map((p) => ({ t: p.d, v: p.growth })),
            }))}
            accessibilityLabel={`Growth of one dollar: ${compare.data.series.map((s) => `${s.name} ${formatRatio(s.points[s.points.length - 1]?.growth ?? '1')} times`).join(', ')}`}
          />
          <MetricsTable
            testID="compare-metrics"
            columns={compare.data.metrics.flatMap((m) =>
              m.metrics ? [{ key: m.strategyId, name: m.name, color: colorOf(m.strategyId), metrics: m.metrics }] : [],
            )}
          />
        </>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  list: { gap: space[3] },
  compare: { gap: space[4] },
  chips: { flexDirection: 'row', flexWrap: 'wrap', gap: space[2] },
});
