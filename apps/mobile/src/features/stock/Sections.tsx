import { color, space } from '@astryx/design-tokens';
import type { ReactNode } from 'react';
import { StyleSheet, View } from 'react-native';

import { Text } from '@/components/Text';

/** An overline title over its content, as used down the Signal tab. */
export function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <View style={styles.section}>
      <Text.Overline tone="tertiary">{title}</Text.Overline>
      {children}
    </View>
  );
}

export function Bullet({ text }: { text: string }) {
  return (
    <View style={styles.bullet}>
      <View style={styles.dot} />
      <Text.Body style={styles.bulletText}>{text}</Text.Body>
    </View>
  );
}

const styles = StyleSheet.create({
  section: { gap: space[2] },
  bullet: { flexDirection: 'row', gap: space[2], alignItems: 'flex-start' },
  dot: { width: 4, height: 4, borderRadius: 2, backgroundColor: color.text3, marginTop: 9 },
  bulletText: { flex: 1 },
});
