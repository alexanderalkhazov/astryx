import { color, radius, space } from '@astryx/design-tokens';
import { router } from 'expo-router';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';

import { Button } from '@/components/Button';
import { Screen } from '@/components/Shell';
import { Text } from '@/components/Text';
import { acceptDisclaimer, DISCLAIMER_POINTS } from '@/features/onboarding/disclaimer';
import { useSession } from '@/state/session';

// T-118 (SYSTEM_DESIGN.md §1, §12.6): shown once per account and device before anything else, after sign-in.
export default function WelcomeScreen() {
  const userId = useSession((s) => s.user?.id);
  const [saving, setSaving] = useState(false);

  const onAccept = async () => {
    if (!userId) return;
    setSaving(true);
    try {
      await acceptDisclaimer(userId);
      router.replace('/');
    } finally {
      setSaving(false);
    }
  };

  return (
    <Screen
      footer={
        <View style={styles.footer}>
          <Button label="I understand" onPress={() => void onAccept()} loading={saving} disabled={!userId} testID="disclaimer-accept" />
          <Button
            variant="link"
            label="Privacy policy"
            onPress={() => router.push('/privacy')}
            style={styles.link}
            testID="disclaimer-privacy"
          />
        </View>
      }
    >
      <View style={styles.content} testID="disclaimer">
        <Text.Overline tone="signal">ASTRYX</Text.Overline>
        <Text.Title accessibilityRole="header">Before you start</Text.Title>
        {DISCLAIMER_POINTS.map((point) => (
          <View key={point.title} style={styles.card} accessible accessibilityLabel={`${point.title}. ${point.body}`}>
            <Text.Section>{point.title}</Text.Section>
            <Text.Body tone="secondary">{point.body}</Text.Body>
          </View>
        ))}
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { gap: space[4], paddingTop: space[6] },
  card: {
    gap: space[1],
    padding: space[4],
    borderRadius: radius.card,
    backgroundColor: color.surface1,
    borderWidth: 1,
    borderColor: color.line,
  },
  footer: { gap: space[3], paddingHorizontal: space[4], paddingTop: space[3], paddingBottom: space[2] },
  link: { alignSelf: 'center' },
});
