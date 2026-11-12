import type { Bar, Quote, Signal } from '@astryx/api-client';
import { isApiError } from '@astryx/api-client';
import { chart as chartSize, color, space } from '@astryx/design-tokens';
import { router, useLocalSearchParams } from 'expo-router';
import { ChartLine, ChevronRight, Search } from 'lucide-react-native';
import { useState } from 'react';
import { Pressable, ScrollView, StyleSheet, View } from 'react-native';

import { Badge, SignalBadge } from '@/components/Badge';
import { Button, IconButton } from '@/components/Button';
import { CandleChart, type CandleOverlays } from '@/components/chart/CandleChart';
import { IndicatorPane, MACD_COLOR, SIGNAL_COLOR } from '@/components/chart/IndicatorPane';
import { EmptyState, Skeleton } from '@/components/Feedback';
import { StackHeader } from '@/components/Navigation';
import { Segmented } from '@/components/Segmented';
import { Screen } from '@/components/Shell';
import { Sheet } from '@/components/Sheet';
import { StatusBanner } from '@/components/StatusBanner';
import { Switch } from '@/components/Switch';
import { Tabs } from '@/components/Tabs';
import { Text } from '@/components/Text';
import { FundamentalsTab } from '@/features/stock/FundamentalsTab';
import { NewsTab } from '@/features/stock/NewsTab';
import { SignalTab } from '@/features/stock/SignalTab';
import { TechnicalsTab } from '@/features/stock/TechnicalsTab';
import { useActiveSignal } from '@/features/stock/useActiveSignal';
import { useScannerStatus } from '@/features/discover/useSignals';
import { TIMEFRAMES, type Timeframe, useIndicators, useStock } from '@/features/stock/useStock';
import { StarButton } from '@/features/watchlist/StarButton';
import { useWatchlistStar } from '@/features/watchlist/useWatchlist';
import { previewSubtract } from '@/lib/decimal';
import { changeDirection, formatEtTime, formatPct, formatPrice, formatRatio, formatSessionDate, formatVolume } from '@/lib/format';
import { useSheet } from '@/state/sheets';

type Tab = 'signal' | 'technicals' | 'fundamentals' | 'news';
const TABS: readonly { value: Tab; label: string }[] = [
  { value: 'signal', label: 'Signal' },
  { value: 'technicals', label: 'Technicals' },
  { value: 'fundamentals', label: 'Fundamentals' },
  { value: 'news', label: 'News' },
];

type OverlayKey = keyof CandleOverlays;
const OVERLAYS: readonly { key: OverlayKey; label: string; swatch: string }[] = [
  { key: 'ma20', label: 'MA 20', swatch: color.series[1] },
  { key: 'ma50', label: 'MA 50', swatch: color.series[3] },
  { key: 'volume', label: 'Volume', swatch: color.text3 },
  { key: 'levels', label: 'Trade levels', swatch: color.signal },
];

const INTRADAY: readonly Timeframe[] = ['1D', '5D'];

// Design 04 / 16 · Stock analysis (SYSTEM_DESIGN.md §8.4.3). The signal ribbon, Signal and Technicals tabs fill in with
// the scanner and quant engine (M3); until then the bottom button is the watchlist toggle.
export default function StockScreen() {
  const { ticker } = useLocalSearchParams<{ ticker: string }>();
  const symbol = (ticker ?? '').toUpperCase();
  const [timeframe, setTimeframe] = useState<Timeframe>('3M');
  const [tab, setTab] = useState<Tab>('signal');
  const [overlays, setOverlays] = useState<Required<CandleOverlays>>({ ma20: true, ma50: true, volume: true, levels: true });
  const [scrubbed, setScrubbed] = useState<Bar | null>(null);
  const [panes, setPanes] = useState({ rsi: true, macd: true });
  const indicatorsSheet = useSheet('indicators');
  const { instrument, quote, chart } = useStock(symbol, timeframe);
  const indicators = useIndicators(symbol, instrument.isSuccess);
  const active = useActiveSignal(symbol, instrument.isSuccess);
  const scannerStatus = useScannerStatus();
  const star = useWatchlistStar(symbol);

  const header = (
    <StackHeader
      title={symbol}
      subtitle={instrument.data?.name}
      sideWidth={88}
      right={
        <>
          <StarButton symbol={symbol} />
          <IconButton icon={Search} variant="plain" accessibilityLabel="Search" onPress={() => router.push('/search')} />
        </>
      }
    />
  );

  if (instrument.isError) {
    const missing = isApiError(instrument.error) && instrument.error.status === 404;
    return (
      <Screen header={header}>
        {missing ? (
          <EmptyState title={`No stock called ${symbol}`} body="Check the ticker, or search by company name." action={{ label: 'Search stocks', onPress: () => router.replace('/search') }} />
        ) : (
          <EmptyState title="Couldn't load this stock" body="Check your connection and try again." action={{ label: 'Retry', onPress: () => void instrument.refetch() }} />
        )}
      </Screen>
    );
  }

  const bars = chart.data?.bars ?? [];
  const visible = bars.slice(chart.data?.warmup ?? 0);
  const readoutBar = scrubbed ?? visible.at(-1) ?? null;
  const levels = chart.data?.levels ?? null;
  const intraday = INTRADAY.includes(timeframe);
  // Trade levels are drawn on 1M and 3M only (design 04).
  const levelsAllowed = timeframe === '1M' || timeframe === '3M';

  const footer = (
    <View style={styles.footer}>
      <View style={styles.footerText}>
        <Text.Data>
          {active.data
            ? `Entry ${formatPrice(active.data.levels.entry)} · R:R ${active.data.metrics.rewardRisk ? formatRatio(active.data.metrics.rewardRisk) : '—'}`
            : quote.data
              ? `Last ${formatPrice(quote.data.last)}`
              : symbol}
        </Text.Data>
        <Text.Caption tone="pending">Research only · not executed</Text.Caption>
      </View>
      <Button
        variant={star.starred ? 'secondary' : 'primary'}
        label={star.starred ? 'On watchlist' : 'Add to watchlist'}
        disabled={!star.ready}
        onPress={star.toggle}
        testID="stock-watchlist-button"
      />
    </View>
  );

  return (
    <>
      <Screen header={header} footer={footer}>
        <PriceBlock quote={quote.data} sector={instrument.data?.industry ?? instrument.data?.sector ?? null} pending={quote.isPending} />
        {quote.data?.stale ? (
          <StatusBanner tone="error" title="Prices may be out of date" body={`Last update ${formatEtTime(quote.data.asOf)}. Values refresh when the data feed recovers.`} />
        ) : null}

        <SignalRibbon signal={active.data} unavailable={active.unavailable} pending={active.isPending} />

        <Readout bar={readoutBar} intraday={intraday} />

        {chart.isError && !chart.data ? (
          <View style={styles.chartBox}>
            <EmptyState title="Price history unavailable" body="The data source didn't answer. Try again shortly." action={{ label: 'Retry', onPress: () => void chart.refetch() }} />
          </View>
        ) : !chart.data ? (
          <View testID="chart-loading">
            <Skeleton height={chartSize.pricePane + chartSize.volumePane + space[2]} />
          </View>
        ) : visible.length === 0 ? (
          <View style={styles.chartBox}>
            {timeframe === '1D' ? (
              <EmptyState title="No bars yet today" body="Intraday bars appear once the session opens at 9:30 ET." />
            ) : (
              <EmptyState title="No price history yet" body="Bars appear after the first market data sync." />
            )}
          </View>
        ) : (
          <View style={{ opacity: chart.isPlaceholderData ? 0.5 : 1 }}>
            <CandleChart
              bars={bars}
              warmup={chart.data.warmup}
              intraday={intraday}
              levels={levelsAllowed ? levels : null}
              overlays={overlays}
              onScrub={(bar) => setScrubbed(bar as Bar | null)}
            />
          </View>
        )}

        <View style={styles.controls}>
          <Segmented
            options={TIMEFRAMES}
            value={timeframe}
            onChange={(value) => {
              setScrubbed(null);
              setTimeframe(value);
            }}
            accessibilityLabel="Chart timeframe"
          />
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="Indicators"
            accessibilityHint="Choose which indicator panes to show"
            onPress={indicatorsSheet.open}
            style={({ pressed }) => [styles.indicatorsButton, { opacity: pressed ? 0.7 : 1 }]}
            testID="indicators-button"
          >
            <ChartLine size={18} strokeWidth={1.7} color={color.text} />
            <Text.Body>Indicators</Text.Body>
          </Pressable>
        </View>
        <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.toggles}>
          {OVERLAYS.filter((o) => o.key !== 'levels' || (levels && levelsAllowed)).map((o) => (
            <OverlayToggle key={o.key} label={o.label} swatch={o.swatch} on={overlays[o.key]} onPress={() => setOverlays((v) => ({ ...v, [o.key]: !v[o.key] }))} />
          ))}
        </ScrollView>

        {/* The panes use daily bars, like the M7 values they read out. */}
        {!intraday && visible.length > 0 && panes.rsi ? (
          <IndicatorPane
            kind="rsi"
            bars={bars}
            warmup={chart.data?.warmup ?? 0}
            readout={<Text.Data testID="rsi-readout">{indicators.data?.values.rsi14 ?? '—'}</Text.Data>}
            accessibilityLabel={`RSI 14, ${indicators.data?.values.rsi14 ?? 'not available'}`}
          />
        ) : null}
        {!intraday && visible.length > 0 && panes.macd ? (
          <IndicatorPane
            kind="macd"
            bars={bars}
            warmup={chart.data?.warmup ?? 0}
            readout={<MacdReadout macd={indicators.data?.values.macd ?? null} signal={indicators.data?.values.macdSignal ?? null} />}
            accessibilityLabel={`MACD ${indicators.data?.values.macd ?? 'not available'}, signal ${indicators.data?.values.macdSignal ?? 'not available'}`}
          />
        ) : null}

        <Tabs tabs={TABS} value={tab} onChange={setTab} />
        {tab === 'signal' ? (
          <SignalTab signal={active.data} pending={active.isPending} unavailable={active.unavailable} evaluatedAt={scannerStatus.data?.lastRunAt} />
        ) : tab === 'technicals' ? (
          <TechnicalsTab snapshot={indicators.data} pending={indicators.isPending} failed={indicators.isError} onRetry={() => void indicators.refetch()} />
        ) : tab === 'fundamentals' ? (
          <FundamentalsTab symbol={symbol} />
        ) : (
          <NewsTab symbol={symbol} />
        )}
      </Screen>
      <Sheet open={indicatorsSheet.isOpen} onClose={indicatorsSheet.close} accessibilityLabel="Indicators">
        <View style={styles.sheet}>
          <Text.Headline>Indicators</Text.Headline>
          <Text.Caption tone="tertiary">Panes under the chart, from daily bars.</Text.Caption>
          {(['rsi', 'macd'] as const).map((key) => (
            <View key={key} style={styles.sheetRow}>
              <Text.Body>{key === 'rsi' ? 'RSI (14)' : 'MACD (12, 26, 9)'}</Text.Body>
              <Switch value={panes[key]} onValueChange={(on) => setPanes((p) => ({ ...p, [key]: on }))} accessibilityLabel={key === 'rsi' ? 'RSI pane' : 'MACD pane'} />
            </View>
          ))}
        </View>
      </Sheet>
    </>
  );
}

function PriceBlock({ quote, sector, pending }: { quote: Quote | undefined; sector: string | null; pending: boolean }) {
  if (pending || !quote) {
    return (
      <View style={styles.price} testID="price-loading">
        <View style={{ gap: space[2] }}>
          <Skeleton width={160} height={32} />
          <Skeleton width={180} height={14} />
        </View>
      </View>
    );
  }
  const direction = changeDirection(quote.change);
  const tone = direction === 'up' ? 'gain' : direction === 'down' ? 'loss' : 'secondary';
  const change = `${formatPrice(quote.change, { signed: true })} (${formatPct(quote.changePct, { signed: true })}) today`;
  return (
    <View style={styles.price} accessible accessibilityLabel={[`${formatPrice(quote.last)}, ${direction} ${formatPct(quote.changePct.replace('-', ''))} today`, sector, `as of ${formatEtTime(quote.asOf)}`].filter(Boolean).join(', ')}>
      <View>
        <Text variant="priceXl" testID="stock-price">
          {formatPrice(quote.last)}
        </Text>
        <Text.Data tone={tone}>{change}</Text.Data>
      </View>
      <View style={styles.priceMeta}>
        {sector ? <Text.Caption tone="secondary">{sector}</Text.Caption> : null}
        <Text.Caption tone="tertiary">{`${formatEtTime(quote.asOf)}${quote.source === 'fake' ? ' · illustrative' : ''}`}</Text.Caption>
      </View>
    </View>
  );
}

/** "Nov 9  O 180.60  H 186.77  L 180.05  C 186.40  V 412.8M" — the last bar, or the bar under the finger. One line that shrinks to fit. */
function Readout({ bar, intraday }: { bar: Bar | null; intraday: boolean }) {
  if (!bar) return <View style={styles.readout} />;
  const up = changeDirection(previewSubtract(bar.c, bar.o)) !== 'down';
  const when = intraday ? formatEtTime(bar.t) : formatSessionDate(bar.d);
  const fields: [string, string, 'primary' | 'gain' | 'loss'][] = [
    ['O', formatPrice(bar.o), 'primary'],
    ['H', formatPrice(bar.h), 'primary'],
    ['L', formatPrice(bar.l), 'primary'],
    ['C', formatPrice(bar.c), up ? 'gain' : 'loss'],
    ['V', formatVolume(bar.v), 'primary'],
  ];
  return (
    <View style={styles.readout} testID="ohlcv-readout" accessible accessibilityLabel={`${when}: open ${bar.o}, high ${bar.h}, low ${bar.l}, close ${bar.c}, volume ${formatVolume(bar.v)}`}>
      <Text.DataSm tone="secondary" numberOfLines={1} adjustsFontSizeToFit minimumFontScale={0.8}>
        {when}
        {fields.map(([label, value, tone]) => (
          <Text.DataSm key={label}>
            <Text.DataSm tone="tertiary">{`  ${label} `}</Text.DataSm>
            <Text.DataSm tone={tone}>{value}</Text.DataSm>
          </Text.DataSm>
        ))}
      </Text.DataSm>
    </View>
  );
}

/** "Breakout · Score 87 · Base Breakout v3 ›" (design 04), or why there is no signal. */
function SignalRibbon({ signal, unavailable, pending }: { signal: Signal | null | undefined; unavailable: boolean; pending: boolean }) {
  if (pending) return <View style={styles.ribbon} />;
  if (!signal) {
    return (
      <View style={styles.ribbon}>
        <Badge label={unavailable ? 'Signals unavailable' : 'No active signal'} />
      </View>
    );
  }

  return (
    <View style={styles.ribbon} testID="signal-ribbon">
      <SignalBadge type={signal.type} />
      <Badge label={`Score ${signal.score}`} />
      <Pressable
        accessibilityRole="link"
        accessibilityLabel={`${signal.strategy.name}, open strategy`}
        onPress={() => router.push({ pathname: '/strategy/[id]', params: { id: signal.strategy.id } })}
        hitSlop={8}
        style={({ pressed }) => [styles.strategyLink, { opacity: pressed ? 0.7 : 1 }]}
      >
        <Text.Body tone="signal">{signal.strategy.name}</Text.Body>
        <ChevronRight size={16} strokeWidth={1.7} color={color.signal} />
      </Pressable>
    </View>
  );
}

function MacdReadout({ macd, signal }: { macd: string | null; signal: string | null }) {
  return (
    <Text.Data testID="macd-readout">
      <Text.Data style={{ color: MACD_COLOR }}>{macd === null ? '—' : formatRatio(macd, 2)}</Text.Data>
      <Text.Data tone="tertiary">{' · '}</Text.Data>
      <Text.Data style={{ color: SIGNAL_COLOR }}>{signal === null ? '—' : formatRatio(signal, 2)}</Text.Data>
    </Text.Data>
  );
}

function OverlayToggle({ label, swatch, on, onPress }: { label: string; swatch: string; on: boolean; onPress: () => void }) {
  return (
    <Pressable
      accessibilityRole="switch"
      accessibilityLabel={label}
      accessibilityState={{ checked: on }}
      onPress={onPress}
      style={({ pressed }) => [styles.toggle, { opacity: pressed ? 0.7 : on ? 1 : 0.5 }]}
    >
      <View style={[styles.swatch, { backgroundColor: swatch }]} />
      <Text.Body>{label}</Text.Body>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  price: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'flex-end', marginTop: space[3] },
  priceMeta: { alignItems: 'flex-end', gap: 2 },
  ribbon: { flexDirection: 'row', alignItems: 'center', gap: space[2], minHeight: 28 },
  strategyLink: { flexDirection: 'row', alignItems: 'center', gap: 2 },
  readout: { minHeight: 18 },
  chartBox: { height: chartSize.pricePane + chartSize.volumePane + space[2], justifyContent: 'center' },
  toggles: { gap: space[2] },
  controls: { flexDirection: 'row', alignItems: 'center', gap: space[2], flexWrap: 'wrap' },
  indicatorsButton: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: space[2],
    minHeight: 44,
    paddingHorizontal: space[3],
    borderRadius: 12,
    borderWidth: 1,
    borderColor: color.lineStrong,
  },
  sheet: { gap: space[3], paddingBottom: space[5] },
  sheetRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', minHeight: 44 },
  toggle: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: space[2],
    minHeight: 40,
    paddingHorizontal: space[3],
    borderRadius: 10,
    borderWidth: 1,
    borderColor: color.lineStrong,
    backgroundColor: color.surface1,
  },
  swatch: { width: 12, height: 2, borderRadius: 1 },
  footer: { flexDirection: 'row', alignItems: 'center', gap: space[3], paddingHorizontal: space[4], paddingVertical: space[3] },
  footerText: { flex: 1, gap: 2 },
});
