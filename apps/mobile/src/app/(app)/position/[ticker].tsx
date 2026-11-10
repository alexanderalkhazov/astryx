import { useLocalSearchParams } from 'expo-router';

import { PlaceholderScreen } from '@/components/PlaceholderScreen';

export default function PositionScreen() {
  const { ticker } = useLocalSearchParams<{ ticker: string }>();
  return <PlaceholderScreen title={`Position · ${ticker}`} design="10, 24" section="§8.4.7" />;
}
