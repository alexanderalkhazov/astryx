import { isApiError, type PositionDetail } from '@astryx/api-client';
import { color, radius, space } from '@astryx/design-tokens';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import Decimal from 'decimal.js-light';
import { randomUUID } from 'expo-crypto';
import { router, useLocalSearchParams } from 'expo-router';
import { CandlestickChart } from 'lucide-react-native';
import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';

import { Badge } from '@/components/Badge';
import { Button, IconButton } from '@/components/Button';
import { LineChart } from '@/components/chart/LineChart';
import { Dialog } from '@/components/Dialog';
import { EmptyState, SkeletonRow } from '@/components/Feedback';
import { StackHeader } from '@/components/Navigation';
import { Screen } from '@/components/Shell';
import { StatusBanner } from '@/components/StatusBanner';
import { Text } from '@/components/Text';
import { market, portfolio } from '@/lib/api';
import { formatEtTime, formatMoney, formatPct, formatPnl, formatPrice, formatQuantity, formatSessionDate } from '@/lib/format';
import { toast } from '@/state/toasts';

const positionKey = (symbol: string) => ['positions', 'detail', symbol] as const;

// Design 10 (position) and 24 (close dialog) · SYSTEM_DESIGN.md §8.4.7.
export default function PositionScreen() {
  const { ticker } = useLocalSearchParams<{ ticker: string }>();
  const symbol = (ticker ?? '').toUpperCase();
  const queryClient = useQueryClient();
  const position = useQuery({
    queryKey: positionKey(symbol),
    queryFn: async () => (await portfolio.positionBySymbol(symbol)).data,
    retry: false,
  });
  const bars = useQuery({
    queryKey: ['stock', symbol, 'bars', '1M'],
    queryFn: async () => (await market.bars(symbol, { timeframe: '1M' })).data.bars,
  });
  const [closing, setClosing] = useState(false);
  const [key, setKey] = useState(() => randomUUID());
  const preview = useQuery({
    queryKey: ['positions', 'close-preview', position.data?.id],
    queryFn: async () => (await portfolio.closePreview(position.data!.id)).data,
    enabled: closing && position.data?.status === 'open',
    retry: false,
  });
  const close = useMutation({
    mutationFn: async () => (await portfolio.close(position.data!.id, key)).data,
    onSuccess: () => {
      setClosing(false);
      void queryClient.invalidateQueries({ queryKey: ['positions'] });
      void queryClient.invalidateQueries({ queryKey: ['portfolio'] });
    },
    onError: (error) =>
      toast({
        tone: 'error',
        title: "The position wasn't closed",
        body: isApiError(error) ? (error.detail ?? error.message) : 'Try again in a moment.',
      }),
  });

  const p = position.data;
  const header = (
    <StackHeader
      title={symbol}
      subtitle={p?.status === 'closed' ? 'Closed position' : 'Open position'}
      right={
        <IconButton
          icon={CandlestickChart}
          variant="plain"
          accessibilityLabel="Stock analysis"
          onPress={() => router.push({ pathname: '/stock/[ticker]', params: { ticker: symbol } })}
        />
      }
    />
  );

  if (position.isError) {
    const missing = isApiError(position.error) && position.error.status === 404;
    return (
      <Screen header={header}>
        <EmptyState
          title={missing ? `No ${symbol} position` : "Couldn't load the position"}
          body={missing ? "You haven't held this stock in your paper account." : 'Check your connection and try again.'}
          action={
            missing
              ? { label: 'Back to Portfolio', onPress: () => router.replace('/portfolio') }
              : { label: 'Retry', onPress: () => void position.refetch() }
          }
        />
      </Screen>
    );
  }

  if (!p) {
    return (
      <Screen header={header}>
        <SkeletonRow />
      </Screen>
    );
  }

  const open = p.status === 'open';
  const footer = open ? (
    <View style={styles.actions}>
      <Button
        variant="secondary"
        label="Analysis"
        block={false}
        style={styles.flex}
        onPress={() => router.push({ pathname: '/stock/[ticker]', params: { ticker: symbol } })}
      />
      <Button
        variant="destructive"
        label="Close position"
        block={false}
        style={styles.flex}
        onPress={() => {
          setKey(randomUUID());
          setClosing(true);
        }}
        testID="close-position"
      />
    </View>
  ) : undefined;

  return (
    <>
      <Screen header={header} footer={footer}>
        {open ? (
          <View style={styles.valueBlock}>
            <View style={styles.row}>
              <Text.Caption tone="secondary">Market value</Text.Caption>
              <Badge label={p.isNew ? 'NEW · SIMULATED' : 'SIMULATED'} tone={p.isNew ? 'signal' : 'neutral'} mono />
            </View>
            <Text.PriceXl>{p.marketValue ? formatMoney(p.marketValue) : '—'}</Text.PriceXl>
            {p.unrealizedPnl ? (
              <Text.DataSm
                tone={new Decimal(p.unrealizedPnl).isNegative() ? 'loss' : 'gain'}
              >{`${formatPnl(p.unrealizedPnl)} (${formatPct(p.unrealizedPct ?? '0', { signed: true })}) unrealized`}</Text.DataSm>
            ) : null}
          </View>
        ) : (
          <StatusBanner
            tone="rejected"
            title="POSITION CLOSED"
            body={`Closed ${p.closedAt ? formatSessionDate(p.closedAt.slice(0, 10)) : ''} at ${p.exitPrice ? formatPrice(p.exitPrice) : '—'} (${p.exitReason ?? 'manual'}). Realized ${p.realizedPnl ? formatPnl(p.realizedPnl) : '—'}. Realized P&L is in Portfolio › Performance.`}
            action={{ label: 'View performance', onPress: () => router.replace('/portfolio') }}
          />
        )}

        {bars.data && bars.data.length > 1 ? (
          <LineChart
            series={[{ key: symbol, points: bars.data.map((b) => ({ t: b.t, v: b.c })) }]}
            height={120}
            accessibilityLabel={`${symbol} over one month`}
          />
        ) : null}

        {open ? <LevelTrack track={p.track} /> : null}

        <View style={styles.facts}>
          <Fact label="Quantity" value={`${formatQuantity(p.quantity)} shares`} />
          <Fact label="Average price" value={formatPrice(p.avgEntryPrice)} />
          {open && p.last ? (
            <Fact label="Last price" value={`${formatPrice(p.last)}${p.quoteAsOf ? ` · ${formatEtTime(p.quoteAsOf)}` : ''}`} />
          ) : null}
          <Fact label="Opened" value={`${formatSessionDate(p.openedAt.slice(0, 10))}${p.entryOrderId ? ` · ${p.entryOrderId}` : ''}`} />
          <Fact label="Fees paid" value={formatMoney(p.feesPaid)} />
        </View>

        {p.generatedBy ? (
          <Pressable
            accessibilityRole="link"
            disabled={!p.generatedBy.strategyId}
            onPress={() =>
              p.generatedBy?.strategyId && router.push({ pathname: '/strategy/[id]', params: { id: p.generatedBy.strategyId } })
            }
            style={({ pressed }) => [styles.card, { opacity: pressed ? 0.7 : 1 }]}
            testID="generated-by"
          >
            <Text.Overline tone="tertiary">Generated by</Text.Overline>
            <Text.Section>
              {p.generatedBy.strategyName ?? (p.generatedBy.source === 'analysis' ? 'AI analysis' : 'Scanner signal')}
            </Text.Section>
            {p.generatedBy.at ? (
              <Text.Caption tone="secondary">{`${p.generatedBy.source === 'analysis' ? 'Analysis' : 'Signal'} ${formatSessionDate(p.generatedBy.at.slice(0, 10))} · ${formatEtTime(p.generatedBy.at)} ›`}</Text.Caption>
            ) : null}
          </Pressable>
        ) : null}
      </Screen>

      <Dialog
        visible={closing}
        overline="PAPER ACCOUNT · SIMULATED"
        title={`Close ${symbol}?`}
        body={
          preview.data
            ? `Sell ${formatQuantity(preview.data.quantity)} shares at about ${formatPrice(preview.data.estExitPrice)} (market value ${formatMoney(preview.data.marketValue)}). Estimated realized P&L ${formatPnl(preview.data.estRealizedPnl)}.`
            : preview.isError
              ? isApiError(preview.error)
                ? (preview.error.detail ?? 'The position can’t be closed right now.')
                : 'The position can’t be closed right now.'
              : 'Checking the latest price…'
        }
        cancelLabel="Keep open"
        confirmLabel="Close position"
        destructive
        busy={close.isPending}
        onCancel={() => setClosing(false)}
        onConfirm={() => preview.data && close.mutate()}
      />
    </>
  );
}

/** Stop · entry · current · target on one track (design 10), positioned by the server's progress (0 = stop, 1 = target). */
function LevelTrack({ track }: { track: PositionDetail['track'] }) {
  const progress = track.progressPct ? new Decimal(track.progressPct).toNumber() : null; // layout only
  return (
    <View
      style={styles.trackWrap}
      accessible
      accessibilityLabel={`Stop ${track.stop ?? '—'}, entry ${track.entry}, current ${track.current ?? '—'}, target ${track.target ?? '—'}`}
      testID="level-track"
    >
      <View style={styles.track}>{progress !== null ? <View style={[styles.current, { left: `${progress * 100}%` }]} /> : null}</View>
      <View style={styles.trackLabels}>
        <Level label="Stop" value={track.stop} tone="loss" />
        <Level label="Entry" value={track.entry} />
        <Level label="Now" value={track.current} />
        <Level label="Target" value={track.target} tone="gain" />
      </View>
    </View>
  );
}

function Level({ label, value, tone }: { label: string; value: string | null; tone?: 'loss' | 'gain' }) {
  return (
    <View style={styles.level}>
      <Text.Caption tone="tertiary">{label}</Text.Caption>
      <Text.DataSm tone={tone}>{value ? formatPrice(value) : '—'}</Text.DataSm>
    </View>
  );
}

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.fact} accessible accessibilityLabel={`${label}: ${value}`}>
      <Text.Body tone="secondary">{label}</Text.Body>
      <Text.Data>{value}</Text.Data>
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  actions: { flexDirection: 'row', gap: space[3] },
  valueBlock: { gap: space[1] },
  row: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' },
  facts: { borderTopWidth: 1, borderTopColor: color.line },
  fact: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    minHeight: 44,
    alignItems: 'center',
    borderBottomWidth: 1,
    borderBottomColor: color.line,
  },
  card: { gap: space[1], padding: space[4], borderRadius: radius.card, backgroundColor: color.surface1 },
  trackWrap: { gap: space[2] },
  track: { height: 6, borderRadius: radius.chip, backgroundColor: color.surface2 },
  current: { position: 'absolute', top: -5, width: 16, height: 16, marginLeft: -8, borderRadius: 8, backgroundColor: color.text },
  trackLabels: { flexDirection: 'row', justifyContent: 'space-between' },
  level: { alignItems: 'center' },
});
