import { color, radius, space } from '@astryx/design-tokens';
import { Redirect, useLocalSearchParams } from 'expo-router';
import { Search } from 'lucide-react-native';
import { useState, type ReactNode } from 'react';
import { ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import { Badge, PaperBadge, SignalBadge } from '@/components/Badge';
import { Button, IconButton } from '@/components/Button';
import { Chip, RemovableChip } from '@/components/Chip';
import { EmptyState, SkeletonRow, Spinner } from '@/components/Feedback';
import { Input, Select } from '@/components/Input';
import { StackHeader } from '@/components/Navigation';
import { Segmented } from '@/components/Segmented';
import { Stepper } from '@/components/Stepper';
import { Switch } from '@/components/Switch';
import { Tabs } from '@/components/Tabs';
import { Text } from '@/components/Text';

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

type SectionKey = 'buttons' | 'badges' | 'inputs' | 'feedback';

function Gallery() {
  // `?only=inputs` renders one section, for side-by-side comparison with the design board.
  const { only } = useLocalSearchParams<{ only?: SectionKey }>();
  const show = (key: SectionKey) => !only || only === key;
  const [chip, setChip] = useState<'all' | 'breakouts'>('all');
  const [timeframe, setTimeframe] = useState<'3M' | '1Y' | '5D'>('3M');
  const [tab, setTab] = useState<'signal' | 'technicals' | 'news'>('signal');
  const [quantity, setQuantity] = useState('76');
  const [on, setOn] = useState(true);
  const [off, setOff] = useState(false);
  const [orderType, setOrderType] = useState<'buy_stop' | 'limit' | 'market'>('buy_stop');

  return (
    <SafeAreaView style={styles.safe} edges={['top']}>
      <StackHeader title="Component gallery" subtitle="Dev only · design 03" />
      <ScrollView contentContainerStyle={styles.content}>
        {show('buttons') && <Section title="Buttons">
          <Button label="Primary · Review proposal" onPress={() => {}} />
          <Button variant="secondary" label="Secondary · View position" onPress={() => {}} />
          <Button variant="destructive" label="Destructive · Close position" onPress={() => {}} />
          <Button label="Disabled · check failed" disabled />
          <Button label="Sending…" loading />
          <View style={styles.row}>
            <IconButton icon={Search} accessibilityLabel="Search" />
            <Button variant="link" label="Text link" onPress={() => {}} />
          </View>
        </Section>}

        {show('badges') && <Section title="Badges, chips & segments">
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
            <PaperBadge />
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
            options={[{ value: '3M', label: '3M' }, { value: '1Y', label: '1Y' }, { value: '5D', label: '5D' }]}
            value={timeframe}
            onChange={setTimeframe}
          />
          <Tabs
            tabs={[{ value: 'signal', label: 'Signal' }, { value: 'technicals', label: 'Technicals' }, { value: 'news', label: 'News' }]}
            value={tab}
            onChange={setTab}
          />
        </Section>}

        {show('inputs') && <Section title="Inputs & controls">
          <Input label="Search" placeholder="Ticker or company" leadingIcon="search" />
          <Select
            label="Order type · dropdown"
            options={[{ value: 'buy_stop', label: 'Buy stop · day' }, { value: 'limit', label: 'Limit · day' }, { value: 'market', label: 'Market' }]}
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
        </Section>}

        {show('feedback') && <Section title="Loading & empty">
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
        </Section>}
      </ScrollView>
    </SafeAreaView>
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
  section: { gap: space[3], padding: space[4], borderRadius: radius.card, borderWidth: 1, borderColor: color.line, backgroundColor: color.surface1 },
  row: { flexDirection: 'row', alignItems: 'center', gap: space[3] },
  wrap: { flexDirection: 'row', flexWrap: 'wrap', gap: space[2] },
});
