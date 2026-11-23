import { color, radius, space } from '@astryx/design-tokens';
import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';

import { Text } from '@/components/Text';
import { useViewMode } from '@/features/plain/mode';
import { type MoneyAction, SAFETY_PROMISE, WHAT_IF } from '@/features/plain/moneyAnswers';
import { useSession } from '@/state/session';

/** T-192: "What happens if…?" beside a money action, collapsed until tapped. Simple mode only. */
export function WhatIf({ action }: { action: MoneyAction }) {
  const simple = useViewMode(useSession((s) => s.user?.id)) === 'simple';
  const [open, setOpen] = useState(false);
  if (!simple) return null;
  return (
    <View style={styles.box} testID={`what-if-${action}`}>
      <Pressable accessibilityRole="button" accessibilityState={{ expanded: open }} onPress={() => setOpen((o) => !o)} hitSlop={8}>
        <Text.Body tone="signal">{open ? 'Hide answers' : 'What happens if…?'}</Text.Body>
      </Pressable>
      {open
        ? WHAT_IF[action].map(({ q, a }) => (
            <View key={q} style={styles.qa}>
              <Text.Section>{q}</Text.Section>
              <Text.Body tone="secondary">{a}</Text.Body>
            </View>
          ))
        : null}
    </View>
  );
}

/** T-192: the calm promise shown at the top of every signed-in screen in Simple mode. */
export function SafetyBadge() {
  const userId = useSession((s) => s.user?.id);
  const simple = useViewMode(userId) === 'simple';
  if (!userId || !simple) return null;
  return (
    <View style={styles.badge} accessible accessibilityRole="text" accessibilityLabel={SAFETY_PROMISE} testID="safety-badge">
      <View style={styles.dot} />
      <Text.Caption tone="secondary">{SAFETY_PROMISE}</Text.Caption>
    </View>
  );
}

const styles = StyleSheet.create({
  box: { gap: space[3], paddingVertical: space[2] },
  qa: { gap: space[1] },
  badge: {
    flexDirection: 'row',
    alignItems: 'center',
    alignSelf: 'center',
    gap: space[2],
    paddingHorizontal: space[3],
    paddingVertical: space[1],
    marginTop: space[1],
    borderRadius: radius.chip,
    backgroundColor: color.surface1,
  },
  dot: { width: 6, height: 6, borderRadius: 3, backgroundColor: color.pending },
});
