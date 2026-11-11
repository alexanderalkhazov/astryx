import { color, space, type as typeScale } from '@astryx/design-tokens';
import { Pressable, StyleSheet, Text, type TextStyle, View } from 'react-native';

export interface TabsProps<T extends string> {
  tabs: readonly { value: T; label: string }[];
  value: T;
  onChange: (value: T) => void;
}

/** Underlined content tabs, e.g. Signal · Technicals · Fundamentals · News (design 03). */
export function Tabs<T extends string>({ tabs, value, onChange }: TabsProps<T>) {
  return (
    <View accessibilityRole="tablist" style={styles.bar}>
      {tabs.map((tab) => {
        const selected = tab.value === value;
        return (
          <Pressable
            key={tab.value}
            accessibilityRole="tab"
            accessibilityState={{ selected }}
            onPress={() => onChange(tab.value)}
            style={({ pressed }) => [styles.tab, { opacity: pressed ? 0.7 : 1 }]}
          >
            <Text style={[styles.label, { color: selected ? color.text : color.text3 }]}>{tab.label}</Text>
            <View style={[styles.underline, { backgroundColor: selected ? color.text : 'transparent' }]} />
          </Pressable>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  bar: { flexDirection: 'row', gap: space[5], borderBottomWidth: 1, borderBottomColor: color.line },
  tab: { minHeight: 44, justifyContent: 'flex-end' },
  label: { ...(typeScale.section as TextStyle), fontFamily: 'Geist_500Medium', paddingBottom: space[2] },
  underline: { height: 2, borderRadius: 1, marginBottom: -1 },
});
