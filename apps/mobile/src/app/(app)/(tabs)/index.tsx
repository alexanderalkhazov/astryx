import { getSystemStatus } from '@astryx/api-client';
import { color, type } from '@astryx/design-tokens';
import { useQuery } from '@tanstack/react-query';
import { Link } from 'expo-router';
import { Text } from 'react-native';

import { PlaceholderScreen } from '@/components/PlaceholderScreen';
import { apiUrl } from '@/lib/config';

export default function HomeScreen() {
  const status = useQuery({ queryKey: ['system-status'], queryFn: ({ signal }) => getSystemStatus(apiUrl, signal) });

  return (
    <PlaceholderScreen title="Overview" design="01 · Home, 22 · Connection lost" section="§8.4.1">
      <Text style={{ ...type.dataSm, color: color.text2 }}>API {apiUrl}</Text>
      {status.isPending && <Text style={{ ...type.dataSm, color: color.text3 }}>Checking backend…</Text>}
      {status.isError && <Text style={{ ...type.dataSm, color: color.loss }}>Backend unreachable: {status.error.message}</Text>}
      {status.data && (
        <Text style={{ ...type.dataSm, color: color.gain }}>
          database {status.data.database} · ai {status.data.ai.status} · broker {status.data.broker.mode}
        </Text>
      )}
      <Link href="/search" style={{ ...type.body, color: color.signal }}>Search</Link>
      <Link href="/stock/NVDA" style={{ ...type.body, color: color.signal }}>Stock · NVDA</Link>
      <Link href="/settings" style={{ ...type.body, color: color.signal }}>Settings</Link>
      <Link href="/sign-in" style={{ ...type.body, color: color.signal }}>Sign in (proposed)</Link>
    </PlaceholderScreen>
  );
}
