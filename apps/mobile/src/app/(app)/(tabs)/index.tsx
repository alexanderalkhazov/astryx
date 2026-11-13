import type { PerformanceRange, Signal } from '@astryx/api-client';
import { color, radius, space } from '@astryx/design-tokens';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import Decimal from 'decimal.js-light';
import { type Href, Link, router } from 'expo-router';
import { useState } from 'react';
import { Pressable, RefreshControl, ScrollView, StyleSheet, View } from 'react-native';

import { SignalBadge } from '@/components/Badge';
import { LineChart } from '@/components/chart/LineChart';
import { SkeletonRow } from '@/components/Feedback';
import { MetricGrid, MetricHero } from '@/components/Metric';
import { Segmented } from '@/components/Segmented';
import { HomeHeader, Screen } from '@/components/Shell';
import { Text } from '@/components/Text';
import { ConnectionBanner } from '@/features/connection/ConnectionBanner';
import { market, notifications, portfolio, scanner } from '@/lib/api';
import { formatEtTime, formatMoney, formatPct, formatPnl, formatPrice, formatRatio } from '@/lib/format';
import { toast } from '@/state/toasts';

/** Screens still waiting for their tasks, reachable in development builds only. */
const DEV_ROUTES: { label: string; href: Href }[] = [
  { label: 'Stock · NVDA', href: '/stock/NVDA' },
  { label: 'Analysis history', href: '/analyses' },
  { label: 'Analysis', href: '/analyses/demo' },
  { label: 'Watchlist (proposed)', href: '/settings/watchlist' },
];

const RANGES: readonly PerformanceRange[] = ['1D', '1W', '1M', '3M', '1Y'];
const CARD_WIDTH = 234;
const CARD_GAP = 12; // snap interval 246 (design 01)

const tone = (value: string) => (new Decimal(value).isNegative() ? 'loss' : 'gain') as 'loss' | 'gain';

// Design 01 · SYSTEM_DESIGN.md §8.4.1: what the portfolio did and what needs attention.
export default function HomeScreen() {
  const queryClient = useQueryClient();
  const [range, setRange] = useState<PerformanceRange>('1M');
  const [scrub, setScrub] = useState<number | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const summary = useQuery({ queryKey: ['portfolio', 'summary'], queryFn: async () => (await portfolio.summary()).data });
  const performance = useQuery({ queryKey: ['portfolio', 'performance', range], queryFn: async () => (await portfolio.performance(range)).data });
  const attention = useQuery({ queryKey: ['portfolio', 'attention'], queryFn: async () => (await portfolio.attention()).data.items });
  const positions = useQuery({ queryKey: ['positions', 'open'], queryFn: async () => (await portfolio.positions('open')).data });
  const opportunities = useQuery({ queryKey: ['signals', 'home'], queryFn: async () => (await scanner.list({ sort: 'score' }, { limit: 8 })).data.items, retry: false });
  const breadth = useQuery({ queryKey: ['market', 'breadth'], queryFn: async () => (await market.breadth()).data, retry: false });
  const unread = useQuery({ queryKey: ['notifications', 'unread'], queryFn: async () => (await notifications.list('all')).data.unreadCount, retry: false });
  const strategies = useQuery({ queryKey: ['portfolio', 'strategies-mtd'], queryFn: async () => (await portfolio.strategyMtd()).data.items, retry: false });

  const refresh = async () => {
    setRefreshing(true);
    try {
      const synced = (await portfolio.sync()).data;
      queryClient.setQueryData(['portfolio', 'summary'], synced);
      await queryClient.invalidateQueries({ queryKey: ['portfolio'] });
      await queryClient.invalidateQueries({ queryKey: ['positions'] });
      toast({ tone: 'success', title: `Synced ${formatEtTime(synced.asOf)}` });
    } catch {
      toast({ tone: 'error', title: "Couldn't sync", body: 'Check your connection and try again.' });
    } finally {
      setRefreshing(false);
    }
  };

  const s = summary.data;
  const p = performance.data;
  const point = p && scrub !== null ? p.series[scrub] : null;

  return (
    <Screen scroll={false}>
      <ScrollView
        contentContainerStyle={styles.content}
        refreshControl={<RefreshControl refreshing={refreshing} onRefresh={() => void refresh()} tintColor={color.text3} />}
        testID="home-scroll"
      >
        <HomeHeader unreadAlerts={(unread.data ?? 0) > 0 || (attention.data?.length ?? 0) > 0} />
        <ConnectionBanner />

        {s ? (
          <MetricHero label="Total portfolio value · paper account" value={point?.equity ?? s.equity}>
            {point ? (
              <Text.DataSm tone="tertiary">{point.t.slice(0, 10)}</Text.DataSm>
            ) : (
              <View style={styles.heroLines}>
                <Text.DataSm tone={tone(s.dayPnl)}>{`${formatPnl(s.dayPnl)}${s.dayPnlPct ? ` (${formatPct(s.dayPnlPct, { signed: true })})` : ''} today`}</Text.DataSm>
                <Text.DataSm tone={tone(s.allTimePnl)}>{`${formatPnl(s.allTimePnl)} (${formatPct(s.allTimeReturnPct, { signed: true })}) all time`}</Text.DataSm>
              </View>
            )}
          </MetricHero>
        ) : (
          <SkeletonRow />
        )}

        {p ? (
          <LineChart
            series={[{ key: 'equity', points: p.series.map((x) => ({ t: x.t, v: x.equity })) }]}
            benchmark={p.series.every((x) => x.benchmark) ? p.series.map((x) => ({ t: x.t, v: x.benchmark! })) : undefined}
            onScrub={setScrub}
            accessibilityLabel={`Portfolio over ${range} against the S&P 500`}
          />
        ) : null}
        <Segmented accessibilityLabel="Chart range" options={RANGES.map((r) => ({ value: r, label: r }))} value={range} onChange={setRange} />
        <Text.Caption tone="tertiary">Dashed line: S&P 500 (SPY), rebased.</Text.Caption>

        {s ? (
          <MetricGrid
            items={[
              { label: 'Cash', value: formatMoney(s.cash) },
              { label: 'Invested', value: formatMoney(s.invested) },
              { label: 'Unrealized', value: formatPnl(s.unrealizedPnl), tone: tone(s.unrealizedPnl) },
            ]}
          />
        ) : null}

        {(attention.data ?? []).length > 0 ? (
          <Section title="Needs attention">
            {attention.data!.map((item) => (
              <Pressable key={`${item.kind}-${item.deepLink}`} accessibilityRole="link" onPress={() => router.push(item.deepLink as Href)} style={({ pressed }) => [styles.attention, { opacity: pressed ? 0.7 : 1 }]}>
                <View style={[styles.dot, { backgroundColor: item.severity === 'error' ? color.loss : item.severity === 'warning' ? color.pending : color.signal }]} />
                <View style={styles.flex}>
                  <Text.Body>{item.title}</Text.Body>
                  <Text.Caption tone="secondary">{item.body}</Text.Caption>
                </View>
              </Pressable>
            ))}
          </Section>
        ) : null}

        {(opportunities.data ?? []).length > 0 ? (
          <Section title="Top opportunities" action={{ label: 'See all', onPress: () => router.push('/discover') }}>
            <ScrollView horizontal showsHorizontalScrollIndicator={false} snapToInterval={CARD_WIDTH + CARD_GAP} decelerationRate="fast" contentContainerStyle={styles.carousel} testID="opportunities-carousel">
              {opportunities.data!.map((signal) => (
                <OpportunityCard key={signal.id} signal={signal} />
              ))}
            </ScrollView>
          </Section>
        ) : null}

        {breadth.data ? (
          <Section title="Market breadth">
            <View style={styles.breadth} accessible accessibilityLabel={`${breadth.data.advancers} advancing, ${breadth.data.decliners} declining, ${formatPct(breadth.data.pctAbove50dma, { dp: 0 })} above their 50-day average`}>
              <Text.Data tone="gain">{`▲ ${breadth.data.advancers}`}</Text.Data>
              <Text.Data tone="loss">{`▼ ${breadth.data.decliners}`}</Text.Data>
              <Text.DataSm tone="secondary">{`${formatPct(breadth.data.pctAbove50dma, { dp: 0 })} above 50-day`}</Text.DataSm>
            </View>
          </Section>
        ) : null}

        {(positions.data?.items ?? []).length > 0 ? (
          <Section title="Open positions" action={{ label: 'Portfolio', onPress: () => router.push('/portfolio') }}>
            {positions.data!.items.slice(0, 5).map((pos) => (
              <Pressable key={pos.id} accessibilityRole="link" onPress={() => router.push({ pathname: '/position/[ticker]', params: { ticker: pos.symbol } })} style={styles.positionRow}>
                <Text.Section style={styles.flex}>{pos.symbol}</Text.Section>
                <Text.Data>{pos.marketValue ? formatMoney(pos.marketValue) : '—'}</Text.Data>
                {pos.unrealizedPnl ? <Text.DataSm tone={tone(pos.unrealizedPnl)}>{formatPnl(pos.unrealizedPnl)}</Text.DataSm> : null}
              </Pressable>
            ))}
          </Section>
        ) : null}

        {(strategies.data ?? []).length > 0 ? (
          <Section title="Strategies · month to date">
            {strategies.data!.map((st) => (
              <Pressable
                key={st.strategyId}
                style={styles.positionRow}
                accessibilityRole="button"
                accessibilityLabel={`${st.name}, ${formatPct(st.pnlPct, { signed: true })} month to date, ${st.trades} trades`}
                onPress={() => router.push({ pathname: '/strategy/[id]', params: { id: st.strategyId } })}
              >
                <Text.Body style={styles.flex}>{st.name}</Text.Body>
                <Text.DataSm tone={tone(st.pnlPct)}>{formatPct(st.pnlPct, { signed: true })}</Text.DataSm>
                <Text.DataSm tone="tertiary">{`${st.trades} trades`}</Text.DataSm>
              </Pressable>
            ))}
          </Section>
        ) : null}

        {__DEV__ ? (
          <View style={styles.dev}>
            <Text.Overline tone="pending">Developer</Text.Overline>
            {DEV_ROUTES.map((route) => (
              <Link key={route.label} href={route.href}>
                <Text tone="signal">{route.label}</Text>
              </Link>
            ))}
            <Link href="/dev/gallery">
              <Text tone="pending">Component gallery (dev)</Text>
            </Link>
          </View>
        ) : null}
      </ScrollView>
    </Screen>
  );
}

function OpportunityCard({ signal }: { signal: Signal }) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={`${signal.symbol}, ${signal.type}, score ${signal.score}`}
      onPress={() => router.push({ pathname: '/stock/[ticker]', params: { ticker: signal.symbol } })}
      style={({ pressed }) => [styles.card, { opacity: pressed ? 0.7 : 1 }]}
    >
      <View style={styles.cardTop}>
        <Text.Section>{signal.symbol}</Text.Section>
        <SignalBadge type={signal.type} />
      </View>
      <Text.Caption tone="secondary" numberOfLines={2}>
        {signal.reason}
      </Text.Caption>
      <Text.DataSm tone="tertiary">{`Score ${signal.score} · Entry ${formatPrice(signal.levels.entry)} · R:R ${signal.metrics.rewardRisk ? formatRatio(signal.metrics.rewardRisk) : '—'}`}</Text.DataSm>
    </Pressable>
  );
}

function Section({ title, action, children }: { title: string; action?: { label: string; onPress: () => void }; children: React.ReactNode }) {
  return (
    <View style={styles.section}>
      <View style={styles.sectionHeader}>
        <Text.Overline tone="tertiary">{title}</Text.Overline>
        {action ? (
          <Pressable accessibilityRole="link" onPress={action.onPress} hitSlop={8}>
            <Text.Caption tone="signal">{action.label}</Text.Caption>
          </Pressable>
        ) : null}
      </View>
      {children}
    </View>
  );
}

const styles = StyleSheet.create({
  content: { gap: space[4], paddingBottom: space[6] },
  heroLines: { gap: 2 },
  section: { gap: space[2] },
  sectionHeader: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  attention: { flexDirection: 'row', gap: space[3], alignItems: 'flex-start', paddingVertical: space[2] },
  dot: { width: 8, height: 8, borderRadius: 4, marginTop: 7 },
  flex: { flex: 1 },
  carousel: { gap: CARD_GAP },
  card: { width: CARD_WIDTH, gap: space[2], padding: space[4], borderRadius: radius.card, backgroundColor: color.surface1 },
  cardTop: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  breadth: { flexDirection: 'row', alignItems: 'center', gap: space[4] },
  positionRow: { flexDirection: 'row', alignItems: 'center', gap: space[3], minHeight: 44 },
  dev: { gap: space[2], marginTop: space[4] },
});
