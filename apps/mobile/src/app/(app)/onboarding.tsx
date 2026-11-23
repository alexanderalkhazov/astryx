import { color, radius, space } from '@astryx/design-tokens';
import { router } from 'expo-router';
import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';

import { Button } from '@/components/Button';
import { Screen } from '@/components/Shell';
import { Text } from '@/components/Text';
import { setViewMode } from '@/features/plain/mode';
import {
  type ExperienceLevel,
  type Horizon,
  LEVEL_LABEL,
  levelFor,
  LIMITS,
  type LossTolerance,
  QUESTIONS,
  saveInvestorProfile,
  useInvestorProfile,
} from '@/features/onboarding/profile';
import { settings } from '@/lib/api';
import { formatPct } from '@/lib/format';
import { useSession } from '@/state/session';
import { toast } from '@/state/toasts';

type Answers = { experience?: ExperienceLevel; horizon?: Horizon; tolerance?: LossTolerance };

// T-162: after the disclaimer (and from Settings › Experience), three questions set the experience level, matching
// risk limits on the server and Simple or Pro explanations.
export default function OnboardingScreen() {
  const userId = useSession((s) => s.user?.id);
  const existing = useInvestorProfile(userId);
  const [picked, setAnswers] = useState<Answers>({});
  // Retaking from Settings starts from the previous answers (experience is stored as the level it produced).
  const answers: Answers = existing
    ? { experience: existing.level, horizon: existing.horizon, tolerance: existing.tolerance, ...picked }
    : picked;
  const [saving, setSaving] = useState(false);
  const complete = answers.experience && answers.horizon && answers.tolerance;
  const level = complete ? levelFor(answers.experience!, answers.tolerance!) : undefined;

  const finish = async () => {
    if (!userId || !level) return;
    setSaving(true);
    try {
      const current = (await settings.risk()).data;
      await settings.updateRisk(current, LIMITS[level]);
      await setViewMode(userId, level === 'experienced' ? 'pro' : 'simple');
      await saveInvestorProfile(userId, { level, horizon: answers.horizon!, tolerance: answers.tolerance! });
      router.replace('/');
    } catch {
      toast({ tone: 'error', title: "Couldn't save your answers", body: 'Check your connection and try again.' });
    } finally {
      setSaving(false);
    }
  };

  return (
    <Screen
      footer={
        <View style={styles.footer}>
          {level ? (
            <Text.Caption tone="secondary" testID="onboarding-summary">
              {`${LEVEL_LABEL[level]}: at most ${formatPct(LIMITS[level].maxRiskPerTradePct)} of your account at risk per trade and ${LIMITS[level].maxOpenPositions} open positions. You can change this in Settings.`}
            </Text.Caption>
          ) : null}
          <Button
            label="Continue"
            onPress={() => void finish()}
            disabled={!level || !userId}
            loading={saving}
            testID="onboarding-continue"
          />
          {existing ? <Button variant="link" label="Cancel" onPress={() => router.back()} testID="onboarding-cancel" /> : null}
        </View>
      }
    >
      <View style={styles.content} testID="onboarding">
        <Text.Overline tone="signal">ASTRYX</Text.Overline>
        <Text.Title accessibilityRole="header">A few questions</Text.Title>
        <Text.Body tone="secondary">So Astryx can set sensible limits and explain things at the right level.</Text.Body>
        {(Object.keys(QUESTIONS) as (keyof typeof QUESTIONS)[]).map((key) => (
          <View key={key} style={styles.question} accessibilityRole="radiogroup" accessibilityLabel={QUESTIONS[key].title}>
            <Text.Section>{QUESTIONS[key].title}</Text.Section>
            {QUESTIONS[key].options.map((option) => {
              const selected = answers[key] === option.value;
              return (
                <Pressable
                  key={option.value}
                  accessibilityRole="radio"
                  accessibilityState={{ selected }}
                  accessibilityLabel={option.label}
                  onPress={() => setAnswers((a) => ({ ...a, [key]: option.value }))}
                  style={({ pressed }) => [styles.option, selected && styles.selected, { opacity: pressed ? 0.8 : 1 }]}
                  testID={`onboarding-${key}-${option.value}`}
                >
                  <Text.Body tone={selected ? 'primary' : 'secondary'}>{option.label}</Text.Body>
                </Pressable>
              );
            })}
          </View>
        ))}
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { gap: space[4], paddingTop: space[6] },
  question: { gap: space[2] },
  option: { padding: space[4], borderRadius: radius.card, borderWidth: 1, borderColor: color.line, backgroundColor: color.surface1 },
  selected: { borderColor: color.signal },
  footer: { gap: space[3], paddingHorizontal: space[4], paddingTop: space[3], paddingBottom: space[2] },
});
