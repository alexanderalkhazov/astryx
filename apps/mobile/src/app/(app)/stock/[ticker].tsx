import { useLocalSearchParams } from 'expo-router';

import { StackHeader } from '@/components/Navigation';
import { Screen } from '@/components/Shell';
import { Text } from '@/components/Text';
import { StarButton } from '@/features/watchlist/StarButton';

// Price, chart and tabs arrive with T-049; the header's watchlist star is live (T-047).
export default function StockScreen() {
  const { ticker } = useLocalSearchParams<{ ticker: string }>();
  return (
    <Screen header={<StackHeader title={ticker} right={<StarButton symbol={ticker} />} />}>
      <Text.Overline tone="pending">Not implemented · 04, 05, 16</Text.Overline>
      <Text.Caption tone="tertiary">Spec: SYSTEM_DESIGN.md §8.4.3</Text.Caption>
    </Screen>
  );
}
