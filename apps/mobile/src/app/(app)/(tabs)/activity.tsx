import { type ActivityItem, getSystemStatus, isApiError, type NotificationCategory, type PaperOrder } from '@astryx/api-client';
import { color, radius, space } from '@astryx/design-tokens';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { randomUUID } from 'expo-crypto';
import { type Href, router, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { Pressable, ScrollView, StyleSheet, View } from 'react-native';

import { Button } from '@/components/Button';
import { Chip } from '@/components/Chip';
import { Dialog } from '@/components/Dialog';
import { SkeletonRow } from '@/components/Feedback';
import { Screen, ScreenHeader } from '@/components/Shell';
import { Text } from '@/components/Text';
import { apiUrl } from '@/lib/config';
import { notifications, paper, scanner } from '@/lib/api';
import { formatEtTime, formatPrice, formatQuantity, formatSessionDate } from '@/lib/format';
import { toast } from '@/state/toasts';

type Filter = NotificationCategory | 'all';
const FILTERS: readonly { value: Filter; label: string }[] = [
  { value: 'all', label: 'All' },
  { value: 'orders', label: 'Orders' },
  { value: 'alerts', label: 'Alerts' },
  { value: 'strategy', label: 'Strategy' },
  { value: 'system', label: 'System' },
];
const SEVERITY: Record<ActivityItem['severity'], string> = {
  info: color.signal,
  // eslint-disable-next-line no-restricted-syntax -- success items are fills and closes: executed states
  success: color.gain,
  warning: color.pending,
  error: color.loss,
};

const etDate = (instant: string) => new Intl.DateTimeFormat('en-CA', { timeZone: 'America/New_York' }).format(new Date(instant)); // YYYY-MM-DD

export const activityKey = (filter: Filter) => ['notifications', filter] as const;

// Design 17 · SYSTEM_DESIGN.md §8.4.10: every fill, alert, strategy event and system issue in one timeline.
export default function ActivityScreen() {
  const params = useLocalSearchParams<{ filter?: string }>();
  const initial = FILTERS.some((f) => f.value === params.filter) ? (params.filter as Filter) : 'all';
  const [filter, setFilter] = useState<Filter>(initial);
  const queryClient = useQueryClient();
  const feed = useQuery({ queryKey: activityKey(filter), queryFn: async () => (await notifications.list(filter)).data });
  const working = useQuery({
    queryKey: ['portfolio', 'working-orders'],
    queryFn: async () => (await paper.working()).data.items.filter((o) => o.purpose === 'entry'),
  });
  const [cancelling, setCancelling] = useState<PaperOrder | null>(null);
  const [cancelKey, setCancelKey] = useState(() => randomUUID());
  const cancel = useMutation({
    mutationFn: async (order: PaperOrder) => (await paper.cancel(order.id, cancelKey)).data,
    onSuccess: () => {
      setCancelling(null);
      void queryClient.invalidateQueries({ queryKey: ['portfolio'] });
    },
    onError: (error) =>
      toast({
        tone: 'error',
        title: "The order wasn't cancelled",
        body: isApiError(error) ? (error.detail ?? error.message) : 'Try again in a moment.',
      }),
  });
  const read = useMutation({
    mutationFn: async (item: ActivityItem) => notifications.markRead(item.id),
    onSettled: () => void queryClient.invalidateQueries({ queryKey: ['notifications'] }),
  });
  const readAll = useMutation({
    mutationFn: async () => notifications.markAllRead(filter === 'all' ? undefined : filter),
    onSettled: () => void queryClient.invalidateQueries({ queryKey: ['notifications'] }),
  });

  const items = feed.data?.items ?? [];
  const groups = items.reduce<{ date: string; items: ActivityItem[] }[]>((acc, item) => {
    const date = etDate(item.createdAt);
    const last = acc.at(-1);
    if (last?.date === date) last.items.push(item);
    else acc.push({ date, items: [item] });
    return acc;
  }, []);
  const today = etDate(new Date().toISOString());

  return (
    <>
      <Screen
        header={
          <ScreenHeader
            title="Activity"
            actions={
              (feed.data?.unreadCount ?? 0) > 0 ? (
                <Button variant="link" label="Mark all read" onPress={() => readAll.mutate()} />
              ) : undefined
            }
          />
        }
      >
        <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.chips}>
          {FILTERS.map((f) => (
            <Chip key={f.value} label={f.label} selected={filter === f.value} onPress={() => setFilter(f.value)} />
          ))}
        </ScrollView>

        {filter === 'all' || filter === 'orders' ? (
          <View style={styles.section}>
            <Text.Overline tone="tertiary">Pending orders</Text.Overline>
            {(working.data ?? []).length === 0 ? (
              <View style={styles.emptyCard} testID="pending-empty">
                <Text.Body tone="secondary">
                  No pending orders — approved buy-stop orders wait here until triggered. Paper fills are immediate.
                </Text.Body>
              </View>
            ) : (
              working.data!.map((o) => (
                <View key={o.id} style={styles.pending} accessible={false} testID={`pending-${o.symbol}`}>
                  <View style={styles.flex}>
                    <Text.Section>{`${o.symbol} · buy stop`}</Text.Section>
                    <Text.Caption tone="secondary">{`${formatQuantity(o.quantity)} sh · triggers at ${formatPrice(o.stopPrice ?? o.limitPrice ?? '0')} · ${o.brokerOrderId}`}</Text.Caption>
                  </View>
                  <Button
                    variant="link"
                    label="Cancel"
                    onPress={() => {
                      setCancelKey(randomUUID());
                      setCancelling(o);
                    }}
                    accessibilityLabel={`Cancel ${o.symbol} order`}
                  />
                </View>
              ))
            )}
          </View>
        ) : null}

        {feed.isPending ? (
          <SkeletonRow />
        ) : items.length === 0 ? (
          <Text.Body tone="secondary">Nothing here yet. Fills, alerts and system events appear as they happen.</Text.Body>
        ) : (
          groups.map((g) => (
            <View key={g.date} style={styles.section}>
              <Text.Overline tone="tertiary">{`${g.date === today ? 'Today · ' : ''}${formatSessionDate(g.date)}`}</Text.Overline>
              {g.items.map((item) => (
                <Pressable
                  key={item.id}
                  accessibilityRole="link"
                  accessibilityLabel={`${item.title}. ${item.body}${item.read ? '' : ', unread'}`}
                  onPress={() => {
                    if (!item.read) read.mutate(item);
                    router.push(item.deepLink as Href);
                  }}
                  style={({ pressed }) => [styles.event, { opacity: pressed ? 0.7 : 1 }]}
                >
                  <View style={[styles.dot, { backgroundColor: SEVERITY[item.severity] }]} />
                  <View style={styles.flex}>
                    {item.read ? <Text.Body>{item.title}</Text.Body> : <Text.Section>{item.title}</Text.Section>}
                    <Text.Caption tone="secondary">{item.body}</Text.Caption>
                  </View>
                  <Text.DataSm tone="tertiary">{formatEtTime(item.createdAt)}</Text.DataSm>
                </Pressable>
              ))}
            </View>
          ))
        )}

        <SystemStatusCard />
      </Screen>

      <Dialog
        visible={cancelling !== null}
        title={cancelling ? `Cancel the ${cancelling.symbol} buy stop?` : 'Cancel order?'}
        body="The order stops working and its reserved cash is released. The proposal stays in history."
        cancelLabel="Keep order"
        confirmLabel="Cancel order"
        destructive
        busy={cancel.isPending}
        onCancel={() => setCancelling(null)}
        onConfirm={() => cancelling && cancel.mutate(cancelling)}
      />
    </>
  );
}

/** Data feed, scanner, AI and broker at a glance (design 17). */
function SystemStatusCard() {
  const status = useQuery({ queryKey: ['system-status'], queryFn: ({ signal }) => getSystemStatus(apiUrl, signal) });
  const scan = useQuery({ queryKey: ['scanner', 'status'], queryFn: async () => (await scanner.status()).data, retry: false });
  const s = status.data;
  const rows: [string, string, boolean][] = s
    ? [
        [
          'Data feed',
          s.marketData.feedDown ? 'Unavailable' : s.marketData.quotesStale ? 'Delayed' : 'Live',
          !s.marketData.feedDown && !s.marketData.quotesStale,
        ],
        [
          'Scanner',
          scan.data ? (scan.data.status === 'live' ? 'Live' : scan.data.status === 'degraded' ? 'Degraded' : 'Unavailable') : '—',
          scan.data?.status === 'live',
        ],
        [
          'AI model',
          s.ai.status === 'ok' || s.ai.status === 'healthy' ? `Ready · ${s.ai.model ?? ''}` : s.ai.status,
          s.ai.status === 'ok' || s.ai.status === 'healthy',
        ],
        ['Broker', `Paper · ${s.broker.adapter ?? 'mock'}`, true],
      ]
    : [];
  return (
    <View style={styles.section} testID="system-status">
      <Text.Overline tone="tertiary">System status</Text.Overline>
      {rows.map(([label, value, okay]) => (
        <View key={label} style={styles.statusRow} accessible accessibilityLabel={`${label}: ${value}`}>
          {/* eslint-disable-next-line no-restricted-syntax -- health dot, as the scanner line on Discover */}
          <View style={[styles.dot, { backgroundColor: okay ? color.gain : color.pending }]} />
          <Text.Body style={styles.flex}>{label}</Text.Body>
          <Text.DataSm tone="secondary">{value}</Text.DataSm>
        </View>
      ))}
      {status.isError ? <Text.Caption tone="loss">Couldn't reach Astryx.</Text.Caption> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  chips: { gap: space[2] },
  section: { gap: space[2] },
  flex: { flex: 1 },
  emptyCard: { padding: space[4], borderRadius: radius.card, borderWidth: 1, borderStyle: 'dashed', borderColor: color.lineStrong },
  pending: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: space[3],
    padding: space[3],
    borderRadius: radius.card,
    backgroundColor: color.surface1,
  },
  event: { flexDirection: 'row', alignItems: 'flex-start', gap: space[3], paddingVertical: space[2] },
  dot: { width: 8, height: 8, borderRadius: 4, marginTop: 7 },
  statusRow: { flexDirection: 'row', alignItems: 'center', gap: space[3], minHeight: 36 },
});
