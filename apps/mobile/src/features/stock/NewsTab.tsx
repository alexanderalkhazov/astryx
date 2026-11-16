import { color, space } from '@astryx/design-tokens';
import { Linking, Pressable, StyleSheet, View } from 'react-native';

import { EmptyState, SkeletonRow } from '@/components/Feedback';
import { Text } from '@/components/Text';
import { formatEtTime, formatSessionDate } from '@/lib/format';

import { useNews } from './useStock';

/** News tab (SYSTEM_DESIGN.md §8.4.3): headlines with source and time; tapping opens the article in the browser. */
export function NewsTab({ symbol }: { symbol: string }) {
  const news = useNews(symbol, true);
  if (news.isPending) return <SkeletonRow />;
  if (news.isError) {
    return (
      <EmptyState
        title="News unavailable"
        body="The news feed didn't answer. Try again shortly."
        action={{ label: 'Retry', onPress: () => void news.refetch() }}
      />
    );
  }
  if (news.data.length === 0) return <EmptyState title="No recent news" body={`Nothing new on ${symbol} from the news source.`} />;

  return (
    <View testID="news-tab">
      {news.data.map((item, index) => {
        const when = `${formatSessionDate(item.publishedAt.slice(0, 10))} · ${formatEtTime(item.publishedAt)}`;
        return (
          <Pressable
            key={item.id}
            accessibilityRole="link"
            accessibilityLabel={`${item.headline}. ${item.source}, ${when}`}
            accessibilityHint="Opens the article in your browser"
            onPress={() => void Linking.openURL(item.url)}
            style={({ pressed }) => [styles.row, index > 0 && styles.divider, { opacity: pressed ? 0.7 : 1 }]}
          >
            <Text.Body>{item.headline}</Text.Body>
            {item.summary ? (
              <Text.Caption tone="secondary" numberOfLines={2}>
                {item.summary}
              </Text.Caption>
            ) : null}
            <Text.Caption tone="tertiary">{`${item.source} · ${when}`}</Text.Caption>
          </Pressable>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  row: { gap: space[1], paddingVertical: space[3] },
  divider: { borderTopWidth: 1, borderTopColor: color.line },
});
