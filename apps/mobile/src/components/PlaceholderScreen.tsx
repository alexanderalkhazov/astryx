import { color, space, type } from '@astryx/design-tokens';
import type { ReactNode } from 'react';
import { ScrollView, StyleSheet, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

type Props = {
  title: string;
  /** Design artboard(s) or "PROPOSED" — see SYSTEM_DESIGN.md §8.2. */
  design: string;
  section: string;
  children?: ReactNode;
};

/** Temporary scaffold for routes whose screens are not implemented yet. */
export function PlaceholderScreen({ title, design, section, children }: Props) {
  return (
    <SafeAreaView style={styles.safe} edges={['top']}>
      <ScrollView contentContainerStyle={styles.content}>
        <Text style={styles.title}>{title}</Text>
        <Text style={styles.overline}>NOT IMPLEMENTED · {design}</Text>
        <Text style={styles.caption}>Spec: SYSTEM_DESIGN.md {section}</Text>
        {children ? <View style={styles.body}>{children}</View> : null}
      </ScrollView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: color.bg },
  content: { padding: space[4], gap: space[2] },
  title: { ...type.title, color: color.text },
  overline: { ...type.overline, color: color.pending },
  caption: { ...type.caption, color: color.text3 },
  body: { marginTop: space[5], gap: space[3] },
});
