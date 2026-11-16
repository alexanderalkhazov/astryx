import { color, radius, space } from '@astryx/design-tokens';
import { useQuery } from '@tanstack/react-query';
import { Redirect, useLocalSearchParams } from 'expo-router';
import { Search } from 'lucide-react-native';
import { useEffect, useState, type ReactNode } from 'react';
import { ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import { Badge, SignalBadge } from '@/components/Badge';
import { Button, IconButton } from '@/components/Button';
import { CheckRow } from '@/components/CheckRow';
import { Chip, RemovableChip } from '@/components/Chip';
import { Dialog } from '@/components/Dialog';
import { EmptyState, SkeletonRow, Spinner } from '@/components/Feedback';
import { Input, Select } from '@/components/Input';
import { MetricGrid, MetricHero, MetricTile, ScoreBreakdownBar } from '@/components/Metric';
import { StackHeader } from '@/components/Navigation';
import { Segmented } from '@/components/Segmented';
import { Sheet } from '@/components/Sheet';
import { PaperChip } from '@/components/Shell';
import { StatusBanner } from '@/components/StatusBanner';
import { Stepper } from '@/components/Stepper';
import { Switch } from '@/components/Switch';
import { Tabs } from '@/components/Tabs';
import { Pnl, Text } from '@/components/Text';
import { CandleChart, type PriceBar } from '@/components/chart/CandleChart';
import { LineChart } from '@/components/chart/LineChart';
import { market } from '@/lib/api';
import { formatPct, formatVolume } from '@/lib/format';
import { useSheet } from '@/state/sheets';
import { toast } from '@/state/toasts';

/**
 * Developer-only component gallery (TASKS.md T-031). Compare against docs/design/png/Component library.png.
 * Release builds redirect away, so it is never reachable by users.
 */
export default function GalleryScreen() {
  if (!__DEV__) {
    return <Redirect href="/" />;
  }

  return <Gallery />;
}

type SectionKey = 'buttons' | 'badges' | 'inputs' | 'feedback' | 'status' | 'metrics' | 'overlays' | 'charts';

function Gallery() {
  // `?only=inputs` renders one section, for side-by-side comparison with the design board.
  const { only, demo } = useLocalSearchParams<{ only?: SectionKey; demo?: 'sheet' | 'dialog' | 'toast' }>();
  const show = (key: SectionKey) => !only || only === key;
  const [chip, setChip] = useState<'all' | 'breakouts'>('all');
  const [timeframe, setTimeframe] = useState<'3M' | '1Y' | '5D'>('3M');
  const [tab, setTab] = useState<'signal' | 'technicals' | 'news'>('signal');
  const [quantity, setQuantity] = useState('76');
  const [on, setOn] = useState(true);
  const [off, setOff] = useState(false);
  const [orderType, setOrderType] = useState<'buy_stop' | 'limit' | 'market'>('buy_stop');
  const [dialogOpen, setDialogOpen] = useState(false);
  const [locked, setLocked] = useState(false);
  const sheet = useSheet('gallery-demo');

  // `?demo=sheet|dialog|toast` opens an overlay on load, for screenshots without tapping.
  useEffect(() => {
    if (demo === 'sheet') sheet.open();
    if (demo === 'dialog') setDialogOpen(true);
    if (demo === 'toast')
      toast({ tone: 'success', title: 'Simulated order filled', body: 'Bought 76 NVDA at $186.52 · stop and target attached' });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [demo]);

  return (
    <SafeAreaView style={styles.safe} edges={['top']}>
      <StackHeader title="Component gallery" subtitle="Dev only · design 03" />
      <ScrollView contentContainerStyle={styles.content}>
        {show('charts') && <ChartsSection />}
        {show('buttons') && (
          <Section title="Buttons">
            <Button label="Primary · Review proposal" onPress={() => {}} />
            <Button variant="secondary" label="Secondary · View position" onPress={() => {}} />
            <Button variant="destructive" label="Destructive · Close position" onPress={() => {}} />
            <Button label="Disabled · check failed" disabled />
            <Button label="Sending…" loading />
            <View style={styles.row}>
              <IconButton icon={Search} accessibilityLabel="Search" />
              <Button variant="link" label="Text link" onPress={() => {}} />
            </View>
          </Section>
        )}

        {show('badges') && (
          <Section title="Badges, chips & segments">
            <View style={styles.wrap}>
              <SignalBadge type="breakout" />
              <SignalBadge type="momentum" />
              <SignalBadge type="pullback" />
              <SignalBadge type="unusual_volume" />
              <SignalBadge type="earnings" />
            </View>
            <View style={styles.wrap}>
              <Badge label="New" tone="gain" mono />
              <Badge label="1.2% to stop" tone="pending" mono />
              <PaperChip />
              <Badge label="Active" tone="gain" />
              <Badge label="Research" />
            </View>
            <View style={styles.wrap}>
              <Chip label="All" count={7} selected={chip === 'all'} onPress={() => setChip('all')} />
              <Chip label="Breakouts" count={2} selected={chip === 'breakouts'} onPress={() => setChip('breakouts')} />
              <RemovableChip label="Mega cap" onRemove={() => {}} />
            </View>
            <Segmented
              accessibilityLabel="Timeframe"
              options={[
                { value: '3M', label: '3M' },
                { value: '1Y', label: '1Y' },
                { value: '5D', label: '5D' },
              ]}
              value={timeframe}
              onChange={setTimeframe}
            />
            <Tabs
              tabs={[
                { value: 'signal', label: 'Signal' },
                { value: 'technicals', label: 'Technicals' },
                { value: 'news', label: 'News' },
              ]}
              value={tab}
              onChange={setTab}
            />
          </Section>
        )}

        {show('inputs') && (
          <Section title="Inputs & controls">
            <Input label="Search" placeholder="Ticker or company" leadingIcon="search" />
            <Select
              label="Order type · dropdown"
              options={[
                { value: 'buy_stop', label: 'Buy stop · day' },
                { value: 'limit', label: 'Limit · day' },
                { value: 'market', label: 'Market' },
              ]}
              value={orderType}
              onChange={setOrderType}
            />
            <Input label="Email" placeholder="you@example.com" defaultValue="not-an-email" error="Enter a valid email address." />
            <Stepper value={quantity} min="1" max="106" unit="shares" onChange={setQuantity} />
            <View style={styles.row}>
              <Switch value={on} onValueChange={setOn} accessibilityLabel="Example switch on" />
              <Switch value={off} onValueChange={setOff} accessibilityLabel="Example switch off" />
              <Text.Caption tone="secondary">Switch · on / off</Text.Caption>
            </View>
          </Section>
        )}

        {show('feedback') && (
          <Section title="Loading & empty">
            {[0, 1, 2].map((i) => (
              <SkeletonRow key={i} index={i} />
            ))}
            <View style={styles.row}>
              <Spinner />
              <Text.Caption tone="secondary">Spinner · inline actions only</Text.Caption>
            </View>
            <EmptyState
              title="No setups match these filters"
              body="Say why it's empty, then offer one clear way out."
              action={{ label: 'Reset filters', onPress: () => {} }}
            />
          </Section>
        )}

        {show('status') && (
          <Section title="Status banners & checks">
            <StatusBanner tone="pending" title="Research proposal — not executed" body="Dashed amber · nothing sent to the broker." />
            <StatusBanner tone="executed" title="Executed · simulated fill" body="Bought 76 NVDA at $186.52" orderId="SIM-463AC2" />
            <StatusBanner
              tone="error"
              title="Connection lost"
              body="Values are from the last sync at 14:12 ET. Approvals are disabled."
              action={{ label: 'Retry', onPress: () => {} }}
            />
            <StatusBanner tone="rejected" title="Proposal rejected" body="No order created. The signal stays in history." />
            <View style={styles.checks}>
              <CheckRow first status="pass" label="Risk per trade · 0.53%" />
              <CheckRow status="warn" label="Technology 22.4% → 34.4%" />
              <CheckRow status="fail" label="Position weight 12.15%" detail="Limit 12%" />
              <CheckRow status="pending" label="Regular session open" />
            </View>
          </Section>
        )}

        {show('metrics') && (
          <Section title="Metric displays">
            <MetricHero label="Total portfolio value" value="118181.30">
              <View style={styles.row}>
                <Pnl value="493.25" />
                <Text.Data tone="gain">({formatPct('0.0042', { signed: true })}) today</Text.Data>
              </View>
            </MetricHero>
            <View style={styles.row}>
              <MetricTile label="Max potential loss" value="−$630.80" tone="loss" caption="0.53% of equity" />
              <MetricTile label="Potential profit" value="+$1,330.00" tone="gain" caption="1.13% of equity" />
            </View>
            <MetricGrid
              items={[
                { label: 'CAGR', value: '+16.9%' },
                { label: 'Sharpe', value: '1.31' },
                { label: 'Max DD', value: '−14.8%' },
                { label: 'PF', value: '1.86' },
              ]}
            />
            <ScoreBreakdownBar label="Pattern quality" value="24" max="30" />
          </Section>
        )}

        {show('overlays') && (
          <Section title="Sheet, dialog & toast">
            <Text.Caption tone="tertiary">
              Sheet store: {sheet.isOpen ? 'open' : 'closed'}
              {locked ? ' · locked' : ''}
            </Text.Caption>
            <Button variant="secondary" label="Open sheet" onPress={sheet.open} />
            <Button variant="destructive" label="Close position…" onPress={() => setDialogOpen(true)} />
            <Button
              variant="secondary"
              label="Show toast"
              onPress={() =>
                toast({ tone: 'success', title: 'Simulated order filled', body: 'Bought 76 NVDA at $186.52 · stop and target attached' })
              }
            />
          </Section>
        )}
      </ScrollView>

      <Sheet open={sheet.isOpen} onClose={sheet.close} dismissible={!locked} accessibilityLabel="Confirm simulated order">
        <Text.Overline tone="pending">Paper account · simulated</Text.Overline>
        <Text.Headline>Confirm simulated order</Text.Headline>
        <View style={styles.row}>
          <Text.Body tone="secondary">Action</Text.Body>
          <Text.Data>Buy 76 · long</Text.Data>
        </View>
        <Button label={locked ? 'Sending… (locked)' : 'Lock as if sending'} onPress={() => setLocked((l) => !l)} />
        <Button variant="link" label="Close" onPress={sheet.close} disabled={locked} />
      </Sheet>

      <Dialog
        visible={dialogOpen}
        title="Close NVDA position?"
        body="Sell 76 NVDA at market (~$14,166.40). Realizes −$9.12. Attached stop and target are cancelled."
        overline="Simulated · paper account"
        cancelLabel="Keep open"
        confirmLabel="Close position"
        destructive
        onCancel={() => setDialogOpen(false)}
        onConfirm={() => setDialogOpen(false)}
      />
    </SafeAreaView>
  );
}

// Design 04 levels for NVDA; real levels come from signals in M3.
const DESIGN_LEVELS = { entry: '186.50', stop: '178.20', target: '204.00' };

/** CandleChart and LineChart over real bars from the local API (T-048). `?only=charts` for the device check. */
function ChartsSection() {
  const bars = useQuery({
    queryKey: ['stock', 'NVDA', 'bars', '1Y'],
    queryFn: async () => (await market.bars('NVDA', { timeframe: '1Y' })).data.bars,
  });
  const [readout, setReadout] = useState<PriceBar | null>(null);
  const [lineIndex, setLineIndex] = useState<number | null>(null);
  if (!bars.data)
    return (
      <Section title="Charts">
        <Spinner />
      </Section>
    );
  const all = bars.data;
  const shown = readout ?? all.at(-1)!;
  const line = all.map((b) => ({ t: b.t, v: b.c }));
  const bench = all.map((b, i) => ({ t: b.t, v: String(Number(all[0]!.c) * (1 + i * 0.0006)) }));
  return (
    <>
      <Section title="CandleChart · NVDA 3M">
        <Text.DataSm tone="secondary" testID="chart-readout">
          {`${shown.d}  O ${shown.o}  H ${shown.h}  L ${shown.l}  C ${shown.c}  V ${formatVolume(shown.v)}`}
        </Text.DataSm>
        <CandleChart bars={all} warmup={Math.max(0, all.length - 63)} levels={DESIGN_LEVELS} onScrub={setReadout} />
      </Section>
      <Section title={`CandleChart · ${all.length} bars (1Y)`}>
        <CandleChart bars={all} overlays={{ levels: false }} />
      </Section>
      <Section title="LineChart · close vs benchmark">
        <Text.DataSm tone="secondary">
          {lineIndex === null ? `Latest ${all.at(-1)!.c}` : `${all[lineIndex]!.d} · ${all[lineIndex]!.c}`}
        </Text.DataSm>
        <LineChart
          series={[{ key: 'NVDA', points: line }]}
          benchmark={bench}
          onScrub={setLineIndex}
          accessibilityLabel="NVDA closes over one year against a benchmark"
        />
      </Section>
    </>
  );
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <View style={styles.section}>
      <Text.Section>{title}</Text.Section>
      {children}
    </View>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: color.bg },
  content: { padding: space[4], gap: space[5], paddingBottom: space[6] * 2 },
  section: {
    gap: space[3],
    padding: space[4],
    borderRadius: radius.card,
    borderWidth: 1,
    borderColor: color.line,
    backgroundColor: color.surface1,
  },
  row: { flexDirection: 'row', alignItems: 'center', gap: space[3] },
  wrap: { flexDirection: 'row', flexWrap: 'wrap', gap: space[2] },
  checks: { borderRadius: radius.card, borderWidth: 1, borderColor: color.line, overflow: 'hidden' },
});
