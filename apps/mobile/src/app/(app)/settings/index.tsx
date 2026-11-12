import { color, space } from '@astryx/design-tokens';
import { router } from 'expo-router';
import { ChevronRight } from 'lucide-react-native';
import { Pressable, StyleSheet, View } from 'react-native';

import { PlaceholderScreen } from '@/components/PlaceholderScreen';
import { Text } from '@/components/Text';
import { useDefaultWatchlist } from '@/features/watchlist/useWatchlist';

// The rest of Settings (designs 18, 25) comes later; the Watchlist row is live (T-047).
export default function SettingsScreen() {
  const watchlist = useDefaultWatchlist();
  const count = watchlist.data?.items.length;
  return (
    <PlaceholderScreen title="Settings" design="18, 25" section="§8.4.11">
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={count === undefined ? 'Watchlist, Manage' : `Watchlist, ${count} ${count === 1 ? 'stock' : 'stocks'}, Manage`}
        onPress={() => router.push('/settings/watchlist')}
        testID="settings-watchlist"
        style={({ pressed }) => [styles.row, { opacity: pressed ? 0.7 : 1 }]}
      >
        <Text.Body style={styles.label}>Watchlist</Text.Body>
        <View style={styles.trailing}>
          {count === undefined ? null : <Text.Data tone="secondary">{count}</Text.Data>}
          <Text.Body tone="secondary">Manage</Text.Body>
          <ChevronRight size={18} strokeWidth={1.7} color={color.text3} />
        </View>
      </Pressable>
    </PlaceholderScreen>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center', minHeight: 44, borderTopWidth: 1, borderBottomWidth: 1, borderColor: color.line, paddingVertical: space[2] },
  label: { flex: 1 },
  trailing: { flexDirection: 'row', alignItems: 'center', gap: space[2] },
});
