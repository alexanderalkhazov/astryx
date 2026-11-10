import { useLocalSearchParams } from 'expo-router';

import { PlaceholderScreen } from '@/components/PlaceholderScreen';

export default function StockScreen() {
  const { ticker } = useLocalSearchParams<{ ticker: string }>();
  return <PlaceholderScreen title={`Stock · ${ticker}`} design="04, 05, 16" section="§8.4.3" />;
}
