import { color, radius, space } from '@astryx/design-tokens';
import { router } from 'expo-router';
import { ChevronRight } from 'lucide-react-native';
import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';

import { Button } from '@/components/Button';
import { Screen } from '@/components/Shell';
import { Text } from '@/components/Text';
import { GOALS, type Goal, saveGoal, useGoal } from '@/features/onboarding/goal';
import { useSession } from '@/state/session';
import { toast } from '@/state/toasts';

// T-190: after the disclaimer and the questions, one plain choice of what to do first (also from Settings › Your goal).
export default function StartScreen() {
  const userId = useSession((s) => s.user?.id);
  const existing = useGoal(userId);
  const [saving, setSaving] = useState<Goal>();

  const choose = async (goal: Goal) => {
    if (!userId || saving) return;
    setSaving(goal);
    try {
      await saveGoal(userId, goal);
      // From Settings, go back to where the user was; the first time, start them where they chose.
      if (existing) router.back();
      else router.replace(GOALS.find((g) => g.value === goal)!.href);
    } catch {
      toast({ tone: 'error', title: "Couldn't save your choice", body: 'Try again.' });
    } finally {
      setSaving(undefined);
    }
  };

  return (
    <Screen
      footer={
        existing ? (
          <View style={styles.footer}>
            <Button variant="link" label="Cancel" onPress={() => router.back()} testID="start-cancel" />
          </View>
        ) : undefined
      }
    >
      <View style={styles.content} testID="start">
        <Text.Overline tone="signal">ASTRYX</Text.Overline>
        <Text.Title accessibilityRole="header">What would you like to do?</Text.Title>
        <Text.Body tone="secondary">Pick one to start. Everything uses practice money, and you can change this any time.</Text.Body>
        {GOALS.map((goal) => {
          const selected = existing === goal.value;
          return (
            <Pressable
              key={goal.value}
              accessibilityRole="button"
              accessibilityLabel={goal.title}
              accessibilityHint={goal.body}
              accessibilityState={{ selected, busy: saving === goal.value }}
              disabled={!userId || saving !== undefined}
              onPress={() => void choose(goal.value)}
              style={({ pressed }) => [styles.choice, selected && styles.selected, { opacity: pressed ? 0.8 : 1 }]}
              testID={`start-${goal.value}`}
            >
              <View style={styles.flex}>
                <Text.Section>{goal.title}</Text.Section>
                <Text.Body tone="secondary">{goal.body}</Text.Body>
              </View>
              <ChevronRight size={20} strokeWidth={1.7} color={color.text3} />
            </Pressable>
          );
        })}
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { gap: space[4], paddingTop: space[6] },
  choice: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: space[3],
    padding: space[5],
    borderRadius: radius.card,
    borderWidth: 1,
    borderColor: color.line,
    backgroundColor: color.surface1,
  },
  selected: { borderColor: color.signal },
  flex: { flex: 1, gap: space[1] },
  footer: { paddingHorizontal: space[4], paddingTop: space[3], paddingBottom: space[2] },
});
