import { getSystemStatus } from '@astryx/api-client';
import { color, space } from '@astryx/design-tokens';
import { useQuery } from '@tanstack/react-query';
import { type Href, Link } from 'expo-router';
import { View } from 'react-native';

import { HomeHeader, SampleDataNotice, Screen } from '@/components/Shell';
import { Pnl, Text } from '@/components/Text';
import { apiUrl } from '@/lib/config';
import { formatPct, splitMoney } from '@/lib/format';

// Illustrative values from the design (01 · Home), shown until T-094 builds the real screen.
const SAMPLE = { equity: '118181.30', dayPnl: '493.25', dayPct: '0.0042', allTime: '10682.05', allTimePct: '0.0994' };

/** Every placeholder route, so the shell can be walked end to end before the screens exist. */
const ROUTES: { label: string; href: Href }[] = [
  { label: 'Stock · NVDA', href: '/stock/NVDA' },
  { label: 'Trade proposal', href: '/proposal/demo' },
  { label: 'Position · NVDA', href: '/position/NVDA' },
  { label: 'Strategy', href: '/strategy/demo' },
  { label: 'Analysis history', href: '/analyses' },
  { label: 'Analysis', href: '/analyses/demo' },
  { label: 'Watchlist (proposed)', href: '/settings/watchlist' },
];

export default function HomeScreen() {
  const status = useQuery({ queryKey: ['system-status'], queryFn: ({ signal }) => getSystemStatus(apiUrl, signal) });
  const hero = splitMoney(SAMPLE.equity);

  return (
    <Screen>
      <HomeHeader unreadAlerts />
      <SampleDataNotice updatedAt="14:32 ET" />
      <View style={{ gap: space[2], marginTop: space[3] }}>
        <Text.Body tone="secondary">Total portfolio value · paper account</Text.Body>
        <Text.Display>
          {hero.main}
          <Text.Display style={{ color: color.text3, fontSize: 24 }}>{hero.cents}</Text.Display>
        </Text.Display>
        <View style={{ flexDirection: 'row', gap: space[2] }}>
          <Pnl value={SAMPLE.dayPnl} />
          <Text.Data tone="gain">({formatPct(SAMPLE.dayPct, { signed: true })}) today</Text.Data>
        </View>
        <View style={{ flexDirection: 'row', gap: space[2] }}>
          <Pnl value={SAMPLE.allTime} />
          <Text.Data tone="gain">({formatPct(SAMPLE.allTimePct, { signed: true })}) all time</Text.Data>
        </View>
      </View>

      <Text.Overline tone="pending" style={{ marginTop: space[5] }}>
        Not implemented · 01, 22 · §8.4.1
      </Text.Overline>
      {status.isPending && <Text.DataSm tone="tertiary">Checking backend…</Text.DataSm>}
      {status.isError && <Text.DataSm tone="loss">Backend unreachable: {status.error.message}</Text.DataSm>}
      {status.data && (
        <Text.DataSm tone="gain">
          database {status.data.database} · ai {status.data.ai.status} · broker {status.data.broker.mode}
        </Text.DataSm>
      )}
      <View style={{ gap: space[2] }}>
        {ROUTES.map((route) => (
          <Link key={route.label} href={route.href}>
            <Text tone="signal">{route.label}</Text>
          </Link>
        ))}
        {__DEV__ ? (
          <Link href="/dev/gallery">
            <Text tone="pending">Component gallery (dev)</Text>
          </Link>
        ) : null}
      </View>
    </Screen>
  );
}
