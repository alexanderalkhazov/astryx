import type { Watchlist, WatchlistItem } from '@astryx/api-client';
import { color, space } from '@astryx/design-tokens';
import { router } from 'expo-router';
import { ArrowDown, ArrowUp, Trash2 } from 'lucide-react-native';
import { useEffect, useState } from 'react';
import { type AccessibilityActionEvent, Pressable, StyleSheet, View } from 'react-native';

import { Button, IconButton } from '@/components/Button';
import { EmptyState, SkeletonRow } from '@/components/Feedback';
import { StackHeader } from '@/components/Navigation';
import { Screen } from '@/components/Shell';
import { StatusBanner } from '@/components/StatusBanner';
import { Text } from '@/components/Text';
import { INTRO } from '@/features/plain/screenIntros';
import { useDefaultWatchlist, useWatchlistEditor } from '@/features/watchlist/useWatchlist';
import { changeDirection, formatPct, formatPrice } from '@/lib/format';

// PROPOSED · Settings › Watchlist (SYSTEM_DESIGN.md §8.3, T-047): the default list, with remove and reorder.
export default function WatchlistScreen() {
  const list = useDefaultWatchlist();
  const editor = useWatchlistEditor();
  const [editingRequested, setEditing] = useState(false);
  const items = list.data?.items ?? [];
  // Removing the last stock ends editing, so the next stock added doesn't open in edit mode.
  const editing = editingRequested && items.length > 0;
  useEffect(() => {
    if (items.length === 0) setEditing(false);
  }, [items.length]);

  const header = (
    <StackHeader
      title="Watchlist"
      sideWidth={64}
      right={
        items.length > 0 ? <Button variant="link" label={editing ? 'Done' : 'Edit'} onPress={() => setEditing((e) => !e)} /> : undefined
      }
    />
  );

  return (
    <Screen intro={INTRO.watchlist} header={header}>
      {list.isError ? (
        <StatusBanner
          tone="error"
          title="Couldn't load your watchlist"
          body="Check your connection and try again."
          action={{ label: 'Retry', onPress: () => void list.refetch() }}
        />
      ) : list.isPending ? (
        <View testID="watchlist-loading">
          <SkeletonRow index={0} />
          <SkeletonRow index={1} />
          <SkeletonRow index={2} />
        </View>
      ) : items.length === 0 || !list.data ? (
        <EmptyState
          title="No stocks yet"
          body="Tap the star on a stock to follow it here."
          action={{ label: 'Search stocks', onPress: () => router.push('/search') }}
        />
      ) : (
        <View>
          <Text.Caption tone="tertiary">
            {items.length} {items.length === 1 ? 'stock' : 'stocks'}
          </Text.Caption>
          {items.map((item, index) => (
            <Row key={item.symbol} list={list.data!} item={item} index={index} editing={editing} editor={editor} />
          ))}
        </View>
      )}
    </Screen>
  );
}

type RowProps = { list: Watchlist; item: WatchlistItem; index: number; editing: boolean; editor: ReturnType<typeof useWatchlistEditor> };

function Row({ list, item, index, editing, editor }: RowProps) {
  const last = list.items.length - 1;
  const direction = item.changePct === null ? null : changeDirection(item.changePct);
  const label = [
    item.symbol,
    item.name,
    item.last ? formatPrice(item.last) : null,
    item.changePct && direction ? `${direction} ${formatPct(item.changePct.replace('-', ''))}` : null,
  ]
    .filter(Boolean)
    .join(', ');

  // VoiceOver users reorder and remove through the rotor in either mode.
  const actions = [
    ...(index > 0 ? [{ name: 'moveUp', label: 'Move up' }] : []),
    ...(index < last ? [{ name: 'moveDown', label: 'Move down' }] : []),
    { name: 'remove', label: 'Remove from watchlist' },
  ];
  const onAction = ({ nativeEvent }: AccessibilityActionEvent) => {
    if (nativeEvent.actionName === 'moveUp') editor.move(list, index, index - 1);
    if (nativeEvent.actionName === 'moveDown') editor.move(list, index, index + 1);
    if (nativeEvent.actionName === 'remove') editor.remove(list, item.symbol);
  };

  return (
    <View style={[styles.row, index > 0 && styles.divider]} testID={`watchlist-row-${item.symbol}`}>
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={label}
        accessibilityActions={actions}
        onAccessibilityAction={onAction}
        disabled={editing}
        onPress={() => router.push({ pathname: '/stock/[ticker]', params: { ticker: item.symbol } })}
        style={({ pressed }) => [styles.main, { opacity: pressed ? 0.7 : 1 }]}
      >
        <View style={styles.identity}>
          <Text.Data style={styles.ticker}>{item.symbol}</Text.Data>
          <Text.Body tone="secondary" numberOfLines={1}>
            {item.name}
          </Text.Body>
        </View>
        {editing ? null : (
          <View style={styles.price}>
            {item.last ? <Text.Data>{formatPrice(item.last)}</Text.Data> : null}
            {item.changePct ? (
              <Text.DataSm tone={direction === 'up' ? 'gain' : direction === 'down' ? 'loss' : 'secondary'}>
                {formatPct(item.changePct, { signed: true })}
              </Text.DataSm>
            ) : null}
          </View>
        )}
      </Pressable>
      {editing ? (
        <View style={styles.controls}>
          <IconButton
            icon={ArrowUp}
            variant="plain"
            accessibilityLabel={`Move ${item.symbol} up`}
            disabled={index === 0}
            onPress={() => editor.move(list, index, index - 1)}
          />
          <IconButton
            icon={ArrowDown}
            variant="plain"
            accessibilityLabel={`Move ${item.symbol} down`}
            disabled={index === last}
            onPress={() => editor.move(list, index, index + 1)}
          />
          <IconButton
            icon={Trash2}
            variant="plain"
            accessibilityLabel={`Remove ${item.symbol}`}
            onPress={() => editor.remove(list, item.symbol)}
          />
        </View>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center', gap: space[2], paddingVertical: space[2] },
  divider: { borderTopWidth: 1, borderTopColor: color.line },
  main: { flex: 1, flexDirection: 'row', alignItems: 'center', gap: space[3], minHeight: 44 },
  identity: { flex: 1, gap: 2 },
  ticker: { fontFamily: 'GeistMono_600SemiBold', fontSize: 16 },
  price: { alignItems: 'flex-end', gap: 2 },
  controls: { flexDirection: 'row' },
});
