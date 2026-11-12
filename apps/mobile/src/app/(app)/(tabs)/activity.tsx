import { useLocalSearchParams } from 'expo-router';

import { PlaceholderScreen } from '@/components/PlaceholderScreen';
import { Text } from '@/components/Text';

export default function ActivityScreen() {
  const { filter = 'all' } = useLocalSearchParams<{ filter?: string }>();
  return (
    <PlaceholderScreen title="Activity" design="17" section="§8.4.10" presentation="tab">
      <Text.DataSm tone="secondary">filter={filter}</Text.DataSm>
    </PlaceholderScreen>
  );
}
