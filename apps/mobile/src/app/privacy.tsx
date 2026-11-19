import { space } from '@astryx/design-tokens';
import { StyleSheet, View } from 'react-native';

import { StackHeader } from '@/components/Navigation';
import { Screen } from '@/components/Shell';
import { Text } from '@/components/Text';

/** What the app does with data, as built (T-118). DRAFT: to be reviewed before any public release (T-130). */
const SECTIONS: { title: string; body: string }[] = [
  {
    title: 'What we store',
    body: 'Your email address and a salted password hash; your watchlist, risk settings and notification preferences; your paper portfolio, proposals, orders and trades; and the AI analyses you request.',
  },
  {
    title: 'Alpaca connection',
    body: 'If you connect Alpaca, your API key and secret are encrypted before they are stored and are only used to place and read paper orders. They are never shown again, logged or stored on your phone. Disconnecting deletes the secret.',
  },
  {
    title: 'On your phone',
    body: 'Only a sign-in token, a random device id and whether you accepted the disclaimer, all in the iOS Keychain or Android Keystore.',
  },
  {
    title: 'Who else sees it',
    body: 'Market data and news come from Alpaca; your Alpaca orders go to Alpaca under its own privacy policy. AI analyses run on a model hosted by the operator, not a third-party AI service. There are no ads and no analytics trackers. If crash reporting is enabled, crash reports are sent without your name or email.',
  },
  {
    title: 'Notifications',
    body: 'If you allow push notifications, your device push token is stored to send fill and stop alerts. Turn them off in Settings at any time.',
  },
  {
    title: 'Your choices',
    body: 'You can change your password, disconnect Alpaca and reset your paper account in Settings. Deleting your account is not in the app yet: ask the operator and it will be removed.',
  },
];

// T-118: reachable signed in (Settings, onboarding) and signed out (sign-up).
export default function PrivacyScreen() {
  return (
    <Screen header={<StackHeader title="Privacy policy" />}>
      <View style={styles.content} testID="privacy-policy">
        <Text.Caption tone="tertiary">Draft · paper-trading prototype · last updated 19 Nov 2026</Text.Caption>
        {SECTIONS.map((section) => (
          <View key={section.title} style={styles.section}>
            <Text.Section accessibilityRole="header">{section.title}</Text.Section>
            <Text.Body tone="secondary">{section.body}</Text.Body>
          </View>
        ))}
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  content: { gap: space[5], paddingVertical: space[4] },
  section: { gap: space[1] },
});
