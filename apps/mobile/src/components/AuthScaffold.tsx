import { color, space } from '@astryx/design-tokens';
import type { ReactNode } from 'react';
import { KeyboardAvoidingView, Platform, ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import { Text } from './Text';

export interface AuthScaffoldProps {
  title: string;
  subtitle?: string;
  children: ReactNode;
  /** Secondary links under the form, e.g. "Create account". */
  footer?: ReactNode;
}

/** Frame for the PROPOSED auth screens (SYSTEM_DESIGN.md §8.2): wordmark, title, form, footer; keyboard-aware. */
export function AuthScaffold({ title, subtitle, children, footer }: AuthScaffoldProps) {
  return (
    <SafeAreaView style={styles.safe} edges={['top', 'bottom']}>
      <KeyboardAvoidingView style={styles.flex} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
          <Text.Overline tone="signal" accessibilityRole="text">
            Astryx
          </Text.Overline>
          <View style={styles.heading}>
            <Text.Title accessibilityRole="header">{title}</Text.Title>
            {subtitle ? <Text.Body tone="secondary">{subtitle}</Text.Body> : null}
          </View>
          <View style={styles.form}>{children}</View>
          {footer ? <View style={styles.footer}>{footer}</View> : null}
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: color.bg },
  flex: { flex: 1 },
  content: { flexGrow: 1, padding: space[4], paddingTop: space[6], gap: space[5] },
  heading: { gap: space[2] },
  form: { gap: space[4] },
  footer: { gap: space[1], marginTop: 'auto', paddingTop: space[5] },
});
