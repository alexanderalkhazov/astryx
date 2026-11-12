import { color } from '@astryx/design-tokens';
import { Star } from 'lucide-react-native';
import { Pressable, StyleSheet } from 'react-native';

import { useWatchlistStar } from './useWatchlist';

/** Watchlist star for a stock header (design 04): filled amber when the symbol is on the default list. */
export function StarButton({ symbol }: { symbol: string }) {
  const { starred, ready, toggle } = useWatchlistStar(symbol);
  return (
    <Pressable
      accessibilityRole="switch"
      accessibilityLabel="Watchlist"
      accessibilityHint={starred ? `Removes ${symbol} from your watchlist` : `Adds ${symbol} to your watchlist`}
      accessibilityState={{ checked: starred, disabled: !ready }}
      disabled={!ready}
      onPress={toggle}
      hitSlop={8}
      testID="watchlist-star"
      style={({ pressed }) => [styles.button, { opacity: !ready ? 0.4 : pressed ? 0.6 : 1 }]}
    >
      <Star size={22} strokeWidth={1.7} color={starred ? color.pending : color.text} fill={starred ? color.pending : 'transparent'} />
    </Pressable>
  );
}

const styles = StyleSheet.create({
  button: { width: 44, height: 44, alignItems: 'center', justifyContent: 'center' },
});
