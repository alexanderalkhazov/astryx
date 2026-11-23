import type { Signal, SignalPage, SignalType } from '@astryx/api-client';
import { color, radius, space } from '@astryx/design-tokens';
import { FlashList } from '@shopify/flash-list';
import { router } from 'expo-router';
import { ActivitySquare, ListFilter, RotateCw, Search, SearchX } from 'lucide-react-native';
import { useEffect, useMemo, useState } from 'react';
import { Pressable, RefreshControl, ScrollView, StyleSheet, View } from 'react-native';

import { Button, IconButton } from '@/components/Button';
import { Chip, RemovableChip } from '@/components/Chip';
import { SkeletonRow } from '@/components/Feedback';
import { OpportunityRow } from '@/components/OpportunityRow';
import { Screen, ScreenHeader } from '@/components/Shell';
import { Text } from '@/components/Text';
import { useInvestorProfile } from '@/features/onboarding/profile';
import { FiltersSheet } from '@/features/discover/FiltersSheet';
import { activeFilterChips, type DiscoverFilters, useDiscoverFilters } from '@/features/discover/filters';
import { isScannerUnavailable, SIGNALS_REFRESH_MS, useScannerStatus, useSignals } from '@/features/discover/useSignals';
import { formatEtTime } from '@/lib/format';
import { useSession } from '@/state/session';

const TYPE_CHIPS: readonly { type: SignalType | undefined; label: string; count: keyof SignalPage['counts'] }[] = [
  { type: undefined, label: 'All', count: 'all' },
  { type: 'momentum', label: 'Momentum', count: 'momentum' },
  { type: 'breakout', label: 'Breakouts', count: 'breakout' },
  { type: 'pullback', label: 'Pullbacks', count: 'pullback' },
  { type: 'unusual_volume', label: 'Unusual volume', count: 'unusualVolume' },
  { type: 'earnings', label: 'Earnings', count: 'earnings' },
];

const SORT_LABEL: Record<DiscoverFilters['sort'], string> = {
  score: 'opportunity score',
  rr: 'risk/reward',
  relStrength: 'relative strength',
  change: 'day change',
};

/** Setups a beginner may see: their strategy made money in a real backtest (T-166). */
const provenForBeginners = (s: Signal) => s.strategy.evidence === 'beats_market' || s.strategy.evidence === 'lags_market';

// Designs 02 (list), 03 (filters), 20 (loading), 21 (empty), 23 (scanner error) · SYSTEM_DESIGN.md §8.4.2.
export default function DiscoverScreen() {
  const userId = useSession((s) => s.user?.id);
  const { filters, set, remove, reset, load } = useDiscoverFilters();
  const [sheetOpen, setSheetOpen] = useState(false);
  useEffect(() => {
    if (userId) void load(userId);
  }, [userId, load]);

  const status = useScannerStatus();
  const signals = useSignals(filters);
  const all = useMemo(() => signals.data?.pages.flatMap((p) => p.items) ?? [], [signals.data]);
  // T-166: beginners only see setups from strategies that made money in their real backtest.
  const beginner = useInvestorProfile(userId)?.level === 'beginner';
  const items = useMemo(() => (beginner ? all.filter((s) => provenForBeginners(s)) : all), [all, beginner]);
  const hiddenForBeginners = all.length - items.length;
  const counts = signals.data?.pages[0]?.counts;
  const unavailable = isScannerUnavailable(signals.error) || status.data?.status === 'unavailable';
  const chips = activeFilterChips(filters);

  const header = (
    <View style={styles.header}>
      <ScreenHeader
        title="Discover"
        actions={
          <>
            <IconButton icon={Search} variant="plain" accessibilityLabel="Search" onPress={() => router.push('/search')} />
            <IconButton
              icon={ListFilter}
              variant="plain"
              accessibilityLabel={chips.length ? `Filters, ${chips.length} active` : 'Filters'}
              onPress={() => setSheetOpen(true)}
              testID="discover-filters"
              badge={chips.length ? <FilterBadge count={chips.length} /> : undefined}
            />
          </>
        }
      />
      <ScannerLine />
      <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.chips}>
        {TYPE_CHIPS.map((c) => (
          <Chip
            key={c.label}
            label={c.label}
            count={counts ? counts[c.count] : undefined}
            selected={filters.type === c.type}
            onPress={() => set({ type: c.type })}
          />
        ))}
      </ScrollView>
      {chips.length ? (
        <View style={styles.activeRow}>
          {chips.map((c) => (
            <RemovableChip key={c.key} label={c.label} onRemove={() => remove(c.key)} />
          ))}
          <Button variant="link" label="Clear all" onPress={() => reset()} />
        </View>
      ) : null}
      {!unavailable ? (
        <View style={styles.listHeader}>
          <Text.Overline
            tone="tertiary"
            numberOfLines={1}
            style={styles.listTitle}
          >{`${items.length}${signals.hasNextPage ? '+' : ''} ${items.length === 1 && !signals.hasNextPage ? 'setup' : 'setups'} · ranked by ${SORT_LABEL[filters.sort]}`}</Text.Overline>
          <Text.Overline tone="tertiary">Illustrative</Text.Overline>
        </View>
      ) : null}
    </View>
  );

  let body;
  if (unavailable) {
    body = <ScannerUnavailable onRetry={() => void Promise.all([status.refetch(), signals.refetch()])} />;
  } else if (signals.isPending) {
    body = (
      <View testID="discover-loading">
        {[0, 1, 2, 3, 4].map((i) => (
          <SkeletonRow key={i} index={i} />
        ))}
      </View>
    );
  } else if (signals.isError) {
    body = <ScannerUnavailable onRetry={() => void signals.refetch()} />;
  } else if (items.length === 0) {
    body = (
      <NoSetups
        filtered={chips.length > 0 || filters.type !== undefined}
        filters={filters}
        onReset={() => reset()}
        onAdjust={() => setSheetOpen(true)}
      />
    );
  } else {
    body = (
      <>
        {hiddenForBeginners > 0 ? (
          <Text.Caption tone="tertiary" testID="discover-hidden-for-beginners">
            {`${hiddenForBeginners} setup${hiddenForBeginners === 1 ? '' : 's'} hidden: their strategy lost money or isn't tested yet. Change your experience level in Settings to see them.`}
          </Text.Caption>
        ) : null}
        <FlashList
          data={items}
          keyExtractor={(s: Signal) => s.id}
          renderItem={({ item }: { item: Signal }) => (
            <OpportunityRow signal={item} onPress={() => router.push({ pathname: '/stock/[ticker]', params: { ticker: item.symbol } })} />
          )}
          onEndReached={() => {
            if (signals.hasNextPage && !signals.isFetchingNextPage) void signals.fetchNextPage();
          }}
          refreshControl={
            <RefreshControl
              refreshing={signals.isRefetching && !signals.isFetchingNextPage}
              onRefresh={() => void signals.refetch()}
              tintColor={color.text3}
            />
          }
          testID="discover-list"
        />
      </>
    );
  }

  return (
    <Screen scroll={false} edges={['top']}>
      {header}
      <View style={styles.body}>{body}</View>
      <FiltersSheet
        open={sheetOpen}
        filters={filters}
        onClose={() => setSheetOpen(false)}
        onApply={(next) => {
          set(next);
          setSheetOpen(false);
        }}
      />
    </Screen>
  );
}

function FilterBadge({ count }: { count: number }) {
  return (
    <View style={styles.badge} accessibilityElementsHidden>
      <Text.DataSm style={styles.badgeText}>{count}</Text.DataSm>
    </View>
  );
}

/** "● Scanner live · 1,031 symbols · Updated 14:32 ET" (design 02). */
function ScannerLine() {
  const status = useScannerStatus();
  if (!status.data) return <View style={styles.statusLine} />;
  const s = status.data;
  // eslint-disable-next-line no-restricted-syntax -- the design's green "● Scanner live" health dot (design 02)
  const tone = s.status === 'live' ? color.gain : s.status === 'degraded' ? color.pending : color.loss;
  const label = s.status === 'live' ? 'Scanner live' : s.status === 'degraded' ? 'Scanner degraded' : 'Scanner unavailable';
  const parts = [
    label,
    `${s.symbolsTotal.toLocaleString('en-US')} symbols`,
    s.lastRunAt ? `Updated ${formatEtTime(s.lastRunAt)}` : null,
  ].filter(Boolean);
  return (
    <View style={styles.statusLine} accessible accessibilityLabel={parts.join(', ')} testID="scanner-status">
      <View style={[styles.dot, { backgroundColor: tone }]} />
      <Text.Caption tone="secondary">{parts.join(' · ')}</Text.Caption>
    </View>
  );
}

/** Design 23: signals hidden, not stale; the code and an automatic retry every minute. */
function ScannerUnavailable({ onRetry }: { onRetry: () => void }) {
  const status = useScannerStatus();
  const code = status.data?.errorCode ?? 'SCN-503';
  const since = status.data?.lastRunAt ? ` since ${formatEtTime(status.data.lastRunAt)}` : '';
  return (
    <View style={styles.state} testID="scanner-unavailable">
      <View style={[styles.stateIcon, { backgroundColor: 'rgba(255,107,107,0.12)' }]}>
        <ActivitySquare size={26} strokeWidth={1.7} color={color.loss} />
      </View>
      <Text.Headline accessibilityRole="header">Scanner feed unavailable</Text.Headline>
      <Text.Body tone="secondary" style={styles.center}>
        {`The scanner has no current results${since}. Signals are hidden rather than shown stale. Open positions and their stops are unaffected.`}
      </Text.Body>
      <Pressable
        onPress={onRetry}
        accessibilityRole="button"
        accessibilityLabel="Retry now"
        style={({ pressed }) => [styles.retry, { opacity: pressed ? 0.7 : 1 }]}
      >
        <RotateCw size={18} strokeWidth={1.7} color={color.text} />
        <Text.Body>Retry now</Text.Body>
      </Pressable>
      <Text.DataSm tone="tertiary">{`${code} · auto-retry in ${SIGNALS_REFRESH_MS / 1000} s`}</Text.DataSm>
    </View>
  );
}

/** Design 21: nothing matches, with one way back and one way forward. */
function NoSetups({
  filtered,
  filters,
  onReset,
  onAdjust,
}: {
  filtered: boolean;
  filters: DiscoverFilters;
  onReset: () => void;
  onAdjust: () => void;
}) {
  const fits = [TYPE_CHIPS.find((c) => c.type === filters.type && c.type)?.label, ...activeFilterChips(filters).map((c) => c.label)]
    .filter(Boolean)
    .join(' · ');
  return (
    <View style={styles.state} testID="discover-empty">
      <View style={styles.stateIcon}>
        <SearchX size={24} strokeWidth={1.7} color={color.text2} />
      </View>
      <Text.Headline accessibilityRole="header">{filtered ? 'No setups match these filters' : 'No setups right now'}</Text.Headline>
      <Text.Body tone="secondary" style={styles.center}>
        {filtered
          ? `Nothing in today's scan fits ${fits}. The scanner re-runs every 15 minutes.`
          : "Nothing in today's scan meets a strategy's rules. The scanner re-runs every 15 minutes."}
      </Text.Body>
      {filtered ? (
        <View style={styles.stateActions}>
          <Button label="Reset filters" block={false} onPress={onReset} style={styles.stateButton} />
          <Button variant="secondary" label="Adjust" block={false} onPress={onAdjust} style={styles.stateButton} />
        </View>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  header: { gap: space[3] },
  chips: { gap: space[2] },
  activeRow: { flexDirection: 'row', alignItems: 'center', flexWrap: 'wrap', gap: space[2] },
  listHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    gap: space[3],
    paddingVertical: space[2],
    borderTopWidth: 1,
    borderBottomWidth: 1,
    borderColor: color.line,
  },
  listTitle: { flex: 1 },
  body: { flex: 1 },
  statusLine: { flexDirection: 'row', alignItems: 'center', gap: space[2], minHeight: 18 },
  dot: { width: 8, height: 8, borderRadius: 4 },
  badge: {
    position: 'absolute',
    top: 4,
    right: 2,
    minWidth: 18,
    height: 18,
    borderRadius: 9,
    backgroundColor: color.signal,
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: 4,
  },
  badgeText: { color: color.bg, fontSize: 11 },
  state: { alignItems: 'center', gap: space[3], paddingTop: space[6], paddingHorizontal: space[4] },
  stateIcon: {
    width: 56,
    height: 56,
    borderRadius: radius.card,
    backgroundColor: color.surface2,
    alignItems: 'center',
    justifyContent: 'center',
  },
  stateActions: { flexDirection: 'row', gap: space[3] },
  stateButton: { paddingHorizontal: space[5] },
  center: { textAlign: 'center' },
  retry: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: space[2],
    minHeight: 48,
    paddingHorizontal: space[5],
    borderRadius: radius.button,
    borderWidth: 1,
    borderColor: color.lineStrong,
  },
});
