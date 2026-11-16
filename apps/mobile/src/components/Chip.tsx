import { color, radius, size, space, type as typeScale } from '@astryx/design-tokens';
import { X } from 'lucide-react-native';
import { Pressable, type PressableProps, StyleSheet, Text, type TextStyle } from 'react-native';

export interface ChipProps extends Omit<PressableProps, 'children' | 'style'> {
  label: string;
  /** Live result count, e.g. "Breakouts 2". */
  count?: number;
  selected?: boolean;
}

/** Filter chip (design 03 · Chips): selected = light fill, unselected = outline. */
export function Chip({ label, count, selected = false, disabled, ...rest }: ChipProps) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ selected, disabled: !!disabled }}
      accessibilityLabel={count === undefined ? label : `${label}, ${count}`}
      disabled={disabled}
      hitSlop={{ top: 4, bottom: 4 }}
      style={({ pressed }) => [
        styles.chip,
        selected ? styles.selected : styles.unselected,
        { opacity: disabled ? 0.4 : pressed ? 0.7 : 1 },
      ]}
      {...rest}
    >
      <Text style={[styles.label, { color: selected ? color.bg : color.text }]}>
        {label}
        {count === undefined ? '' : ` ${count}`}
      </Text>
    </Pressable>
  );
}

export interface RemovableChipProps extends Omit<PressableProps, 'children' | 'style' | 'onPress'> {
  label: string;
  onRemove: () => void;
}

/** Active filter that can be removed with one tap, e.g. "Mega cap ×". */
export function RemovableChip({ label, onRemove, ...rest }: RemovableChipProps) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={`Remove filter ${label}`}
      onPress={onRemove}
      style={({ pressed }) => [styles.chip, styles.removable, { opacity: pressed ? 0.7 : 1 }]}
      {...rest}
    >
      <Text style={[styles.label, { color: color.signal }]}>{label}</Text>
      <X size={14} strokeWidth={1.7} color={color.signal} />
    </Pressable>
  );
}

const styles = StyleSheet.create({
  chip: {
    minHeight: 36,
    minWidth: size.touch,
    borderRadius: radius.chip,
    paddingHorizontal: space[3],
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: space[1],
  },
  selected: { backgroundColor: color.text },
  unselected: { borderWidth: 1, borderColor: color.lineStrong },
  removable: { borderWidth: 1, borderColor: 'rgba(138,180,255,0.5)', backgroundColor: 'rgba(138,180,255,0.10)' },
  label: { ...(typeScale.body as TextStyle), lineHeight: undefined, fontFamily: 'Geist_500Medium' },
});
