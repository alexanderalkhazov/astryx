import { color, radius, space, type as typeScale } from '@astryx/design-tokens';
import { Pressable, StyleSheet, Text, type TextStyle, View } from 'react-native';

export interface SegmentOption<T extends string> {
  value: T;
  label: string;
  disabled?: boolean;
}

export interface SegmentedProps<T extends string> {
  options: readonly SegmentOption<T>[];
  value: T;
  onChange: (value: T) => void;
  /** Mono labels for timeframes and percentages (design: "3M · 1Y · 5D", "0.50% · 0.75%"). */
  mono?: boolean;
  accessibilityLabel: string;
}

/** Segmented control for timeframes, risk % and Paper/Live (design 03). */
export function Segmented<T extends string>({ options, value, onChange, mono = true, accessibilityLabel }: SegmentedProps<T>) {
  return (
    <View accessibilityRole="radiogroup" accessibilityLabel={accessibilityLabel} style={styles.track}>
      {options.map((option) => {
        const selected = option.value === value;
        return (
          <Pressable
            key={option.value}
            accessibilityRole="radio"
            accessibilityState={{ selected, disabled: !!option.disabled }}
            accessibilityLabel={option.label}
            disabled={option.disabled}
            onPress={() => {
              if (!selected) onChange(option.value);
            }}
            style={({ pressed }) => [styles.segment, selected ? styles.selected : null, { opacity: option.disabled ? 0.35 : pressed ? 0.7 : 1 }]}
          >
            <Text style={[mono ? styles.mono : styles.sans, { color: selected ? color.text : color.text3 }]}>{option.label}</Text>
          </Pressable>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  track: { flexDirection: 'row', backgroundColor: color.surface2, borderRadius: radius.button, padding: 4, gap: 2, alignSelf: 'flex-start' },
  segment: { minHeight: 36, minWidth: 44, paddingHorizontal: space[3], borderRadius: radius.chip, alignItems: 'center', justifyContent: 'center' },
  selected: { backgroundColor: color.lineStrong },
  mono: { ...(typeScale.dataSm as TextStyle), fontFamily: 'GeistMono_600SemiBold' },
  sans: { ...(typeScale.body as TextStyle), lineHeight: undefined, fontFamily: 'Geist_500Medium' },
});
