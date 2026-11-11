import { getSystemStatus } from '@astryx/api-client';
import { color, space } from '@astryx/design-tokens';
import { useQuery } from '@tanstack/react-query';
import { Link } from 'expo-router';
import { View } from 'react-native';

import { PlaceholderScreen } from '@/components/PlaceholderScreen';
import { Pnl, Text } from '@/components/Text';
import { apiUrl } from '@/lib/config';
import { formatPct, formatPrice, splitMoney } from '@/lib/format';

// Illustrative values from the design (01 · Home), shown to preview typography until T-094 builds the real screen.
const SAMPLE = { equity: '118181.30', dayPnl: '493.25', dayPct: '0.0042', allTime: '10682.05', loss: '-630.80', price: '186.40' };

export default function HomeScreen() {
  const status = useQuery({ queryKey: ['system-status'], queryFn: ({ signal }) => getSystemStatus(apiUrl, signal) });
  const hero = splitMoney(SAMPLE.equity);

  return (
    <PlaceholderScreen title="Overview" design="01 · Home, 22 · Connection lost" section="§8.4.1">
      <View style={{ gap: space[2] }}>
        <Text.Caption tone="tertiary">Illustrative sample data — not live prices</Text.Caption>
        <Text.Display>
          {hero.main}
          <Text.Display style={{ color: color.text3, fontSize: 24 }}>{hero.cents}</Text.Display>
        </Text.Display>
        <View style={{ flexDirection: 'row', gap: space[2] }}>
          <Pnl value={SAMPLE.dayPnl} />
          <Text.Data tone="gain">({formatPct(SAMPLE.dayPct, { signed: true })}) today</Text.Data>
        </View>
        <Pnl value={SAMPLE.allTime} />
        <View style={{ flexDirection: 'row', gap: space[4], marginTop: space[3] }}>
          <Text.PriceXl>{formatPrice(SAMPLE.price)}</Text.PriceXl>
          <Pnl value={SAMPLE.loss} variant="data" />
        </View>
        <Text.Overline tone="pending">Research proposal · not executed</Text.Overline>
      </View>
      <Text.DataSm tone="secondary">API {apiUrl}</Text.DataSm>
      {status.isPending && <Text.DataSm tone="tertiary">Checking backend…</Text.DataSm>}
      {status.isError && <Text.DataSm tone="loss">Backend unreachable: {status.error.message}</Text.DataSm>}
      {status.data && (
        <Text.DataSm tone="gain">
          database {status.data.database} · ai {status.data.ai.status} · broker {status.data.broker.mode}
        </Text.DataSm>
      )}
      <Link href="/search"><Text tone="signal">Search</Text></Link>
      <Link href="/stock/NVDA"><Text tone="signal">Stock · NVDA</Text></Link>
      <Link href="/settings"><Text tone="signal">Settings</Text></Link>
      <Link href="/sign-in"><Text tone="signal">Sign in (proposed)</Text></Link>
    </PlaceholderScreen>
  );
}
