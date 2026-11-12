import type { SearchItem } from '@astryx/api-client';
import { color, space } from '@astryx/design-tokens';
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { router } from 'expo-router';
import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';

import { Badge, SignalBadge, type SignalType, signalBadge } from '@/components/Badge';
import { Button } from '@/components/Button';
import { Chip } from '@/components/Chip';
import { EmptyState, SkeletonRow } from '@/components/Feedback';
import { Input } from '@/components/Input';
import { Screen } from '@/components/Shell';
import { StatusBanner } from '@/components/StatusBanner';
import { Text } from '@/components/Text';
import { useDebouncedValue } from '@/hooks/useDebouncedValue';
import { search } from '@/lib/api';
import { changeDirection, formatPct, formatPrice } from '@/lib/format';

export const RECENT_KEY = ['search', 'recent'] as const;

// Design 19 · Search (SYSTEM_DESIGN.md §8.4.12): full-screen modal over any tab.
export default function SearchScreen() {
  const queryClient = useQueryClient();
  const [text, setText] = useState('');
  const query = useDebouncedValue(text.trim(), 200);

  const recent = useQuery({ queryKey: RECENT_KEY, queryFn: async () => (await search.recent()).data.items });
  const results = useQuery({
    queryKey: ['search', 'q', query],
    queryFn: async ({ signal }) => (await search.search(query, 20, signal)).data.items,
    enabled: query.length > 0,
    placeholderData: keepPreviousData,
  });

  const open = (symbol: string) => {
    // Recording the recent must never block opening the stock.
    search
      .addRecent(symbol)
      .then(() => queryClient.invalidateQueries({ queryKey: RECENT_KEY }))
      .catch(() => undefined);
    router.replace({ pathname: '/stock/[ticker]', params: { ticker: symbol } });
  };

  const header = (
    <View style={styles.bar}>
      <View style={styles.field}>
        <Input
          leadingIcon="search"
          value={text}
          onChangeText={setText}
          placeholder="Ticker or company"
          accessibilityLabel="Search"
          autoFocus
          autoCapitalize="characters"
          autoCorrect={false}
          returnKeyType="search"
          testID="search-input"
        />
      </View>
      <Button variant="link" label="Cancel" onPress={() => router.back()} />
    </View>
  );

  return (
    <Screen header={header}>
      {recent.data && recent.data.length > 0 ? (
        <View style={styles.section}>
          <Text.Overline tone="tertiary">Recent</Text.Overline>
          <View style={styles.chips}>
            {recent.data.map((item) => (
              <Chip key={item.symbol} label={item.symbol} onPress={() => open(item.symbol)} accessibilityHint={`Opens ${item.name}`} />
            ))}
          </View>
        </View>
      ) : null}

      {query.length === 0 ? null : results.isError ? (
        <StatusBanner tone="error" title="Search failed" body="Check your connection and try again." action={{ label: 'Retry', onPress: () => void results.refetch() }} />
      ) : results.isPending ? (
        <View testID="search-loading">
          <SkeletonRow index={0} />
          <SkeletonRow index={1} />
          <SkeletonRow index={2} />
        </View>
      ) : results.data.length === 0 ? (
        <EmptyState title={`No matches for '${query}'`} body="Try a ticker such as NVDA or part of a company name." />
      ) : (
        <View style={styles.section}>
          <Text.Caption tone="tertiary">
            {results.data.length} {results.data.length === 1 ? 'result' : 'results'}
          </Text.Caption>
          <View>
            {results.data.map((item, index) => (
              <ResultRow key={item.symbol} item={item} first={index === 0} onPress={() => open(item.symbol)} />
            ))}
          </View>
        </View>
      )}
    </Screen>
  );
}

function ResultRow({ item, first, onPress }: { item: SearchItem; first: boolean; onPress: () => void }) {
  const change = item.changePct;
  const direction = change === null ? null : changeDirection(change);
  const signal = item.signalType && item.signalType in signalBadge ? (item.signalType as SignalType) : null;
  const label = [
    item.symbol,
    item.name,
    item.held ? 'held' : null,
    signal ? `${signalBadge[signal].label} signal` : null,
    item.last ? formatPrice(item.last) : null,
    change ? `${direction} ${formatPct(change.replace('-', ''))}` : null,
  ]
    .filter(Boolean)
    .join(', ');

  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={label}
      onPress={onPress}
      testID={`search-result-${item.symbol}`}
      style={({ pressed }) => [styles.row, !first && styles.divider, { opacity: pressed ? 0.7 : 1 }]}
    >
      <View style={styles.identity}>
        <View style={styles.tickerLine}>
          <Text.Data style={styles.ticker}>{item.symbol}</Text.Data>
          {item.held ? <Badge label="Held" /> : null}
          {signal ? <SignalBadge type={signal} /> : null}
        </View>
        <Text.Body tone="secondary" numberOfLines={1}>
          {item.name}
        </Text.Body>
      </View>
      <View style={styles.price}>
        {item.last ? <Text.Data>{formatPrice(item.last)}</Text.Data> : null}
        {change ? (
          <Text.DataSm tone={direction === 'up' ? 'gain' : direction === 'down' ? 'loss' : 'secondary'}>{formatPct(change, { signed: true })}</Text.DataSm>
        ) : null}
      </View>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  bar: { flexDirection: 'row', alignItems: 'center', gap: space[3], paddingHorizontal: space[4], paddingVertical: space[2], borderBottomWidth: 1, borderBottomColor: color.line },
  field: { flex: 1 },
  section: { gap: space[3], marginTop: space[3] },
  chips: { flexDirection: 'row', flexWrap: 'wrap', gap: space[2] },
  row: { flexDirection: 'row', alignItems: 'center', gap: space[3], paddingVertical: space[3] },
  divider: { borderTopWidth: 1, borderTopColor: color.line },
  identity: { flex: 1, gap: 2 },
  tickerLine: { flexDirection: 'row', alignItems: 'center', gap: space[2] },
  ticker: { fontFamily: 'GeistMono_600SemiBold', fontSize: 16 },
  price: { alignItems: 'flex-end', gap: 2 },
});
