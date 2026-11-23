import { color, radius, space } from '@astryx/design-tokens';
import { useQuery } from '@tanstack/react-query';
import Decimal from 'decimal.js-light';
import { router } from 'expo-router';
import { Pressable, StyleSheet, View } from 'react-native';

import { Button } from '@/components/Button';
import { SkeletonRow } from '@/components/Feedback';
import { OpportunityRow } from '@/components/OpportunityRow';
import { PaperChip, Screen, ScreenHeader } from '@/components/Shell';
import { Text } from '@/components/Text';
import { useInvestorProfile } from '@/features/onboarding/profile';
import { provenForBeginners } from '@/features/strategies/proven';
import { portfolio, scanner } from '@/lib/api';
import { formatMoney, formatPnl } from '@/lib/format';
import { useSession } from '@/state/session';

// T-194: Simple mode's trading tab: your practice trades and today's ideas, filtered for beginners like Discover.
export default function PracticeScreen() {
  const beginner = useInvestorProfile(useSession((s) => s.user?.id))?.level === 'beginner';
  const positions = useQuery({ queryKey: ['positions', 'open'], queryFn: async () => (await portfolio.positions('open')).data });
  const ideas = useQuery({
    queryKey: ['signals', 'practice'],
    queryFn: async () => (await scanner.list({ sort: 'score' }, { limit: 20 })).data.items,
    retry: false,
  });
  const shown = (ideas.data ?? []).filter((s) => !beginner || provenForBeginners(s)).slice(0, 5);
  const open = positions.data?.items ?? [];

  return (
    <Screen header={<ScreenHeader title="Practice" chip={<PaperChip />} />}>
      <View style={styles.content} testID="practice">
        <Text.Body tone="secondary">Try trading single stocks with pretend money. Your trades and today’s ideas are here.</Text.Body>

        {positions.data && open.length === 0 ? (
          <View style={styles.card} testID="practice-guided">
            <Text.Section>Your first practice trade</Text.Section>
            <Text.Body tone="secondary">We’ll walk you through one of today’s ideas step by step. It takes about two minutes.</Text.Body>
            <Button label="Start the guided trade" onPress={() => router.push('/guided-trade')} />
          </View>
        ) : null}

        <Text.Overline tone="tertiary">YOUR PRACTICE TRADES</Text.Overline>
        {!positions.data ? (
          <SkeletonRow />
        ) : open.length === 0 ? (
          <Text.Body tone="secondary">No trades yet. A trade you approve shows up here.</Text.Body>
        ) : (
          open.map((pos) => (
            <Pressable
              key={pos.id}
              accessibilityRole="link"
              onPress={() => router.push({ pathname: '/position/[ticker]', params: { ticker: pos.symbol } })}
              style={({ pressed }) => [styles.row, { opacity: pressed ? 0.7 : 1 }]}
            >
              <Text.Section style={styles.flex}>{pos.symbol}</Text.Section>
              <Text.Data>{pos.marketValue ? formatMoney(pos.marketValue) : '—'}</Text.Data>
              {pos.unrealizedPnl ? (
                <Text.DataSm tone={new Decimal(pos.unrealizedPnl).isNegative() ? 'loss' : 'gain'}>
                  {formatPnl(pos.unrealizedPnl)}
                </Text.DataSm>
              ) : null}
            </Pressable>
          ))
        )}

        <Text.Overline tone="tertiary">TODAY’S IDEAS</Text.Overline>
        {ideas.isPending ? (
          <SkeletonRow />
        ) : shown.length === 0 ? (
          <Text.Body tone="secondary" testID="practice-no-ideas">
            {beginner && (ideas.data ?? []).length > 0
              ? 'No ideas from tested strategies yet. A strategy needs a live trial before beginners see its ideas; the guided trade still works.'
              : 'No ideas right now. New ones appear while the market is open.'}
          </Text.Body>
        ) : (
          shown.map((signal) => (
            <OpportunityRow
              key={signal.id}
              signal={signal}
              onPress={() => router.push({ pathname: '/stock/[ticker]', params: { ticker: signal.symbol } })}
            />
          ))
        )}
        <Button variant="link" label="All ideas and filters" onPress={() => router.push('/discover')} testID="practice-discover" />
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { gap: space[3], paddingBottom: space[6] },
  card: { gap: space[2], padding: space[4], borderRadius: radius.card, backgroundColor: color.surface1 },
  row: { flexDirection: 'row', alignItems: 'center', gap: space[3], minHeight: 44 },
  flex: { flex: 1 },
});
