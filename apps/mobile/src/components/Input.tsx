import { color, radius, size, space, type as typeScale } from '@astryx/design-tokens';
import { Check, ChevronDown, Search } from 'lucide-react-native';
import { type ComponentRef, forwardRef, useState } from 'react';
import { Pressable, StyleSheet, Text, TextInput, type TextInputProps, type TextStyle, View } from 'react-native';

export interface InputProps extends TextInputProps {
  label?: string;
  /** Inline validation message (e.g. from a 422 field error). */
  error?: string;
  leadingIcon?: 'search';
}

/** Text field (design 03 · Inputs): surface-2 fill, hairline border, signal border when focused, red when invalid. */
export const Input = forwardRef<ComponentRef<typeof TextInput>, InputProps>(function Input(
  { label, error, leadingIcon, style, onFocus, onBlur, ...rest },
  ref,
) {
  const [focused, setFocused] = useState(false);
  const borderColor = error ? color.loss : focused ? color.signal : color.line;
  return (
    <View style={styles.field}>
      {label ? <Text style={styles.label}>{label}</Text> : null}
      <View style={[styles.box, { borderColor }]}>
        {leadingIcon === 'search' ? <Search size={18} strokeWidth={1.7} color={color.text3} /> : null}
        <TextInput
          ref={ref}
          placeholderTextColor={color.text3}
          selectionColor={color.signal}
          accessibilityLabel={rest.accessibilityLabel ?? label ?? rest.placeholder}
          accessibilityHint={error}
          onFocus={(e) => {
            setFocused(true);
            onFocus?.(e);
          }}
          onBlur={(e) => {
            setFocused(false);
            onBlur?.(e);
          }}
          style={[styles.input, style]}
          {...rest}
        />
      </View>
      {error ? (
        <Text style={styles.error} accessibilityLiveRegion="polite">
          {error}
        </Text>
      ) : null}
    </View>
  );
});

export interface SelectOption<T extends string> {
  value: T;
  label: string;
}

export interface SelectProps<T extends string> {
  label?: string;
  options: readonly SelectOption<T>[];
  value: T;
  onChange: (value: T) => void;
}

/** Dropdown that opens an inline list (design 03 · "Order type · dropdown"); the selected row shows a check. */
export function Select<T extends string>({ label, options, value, onChange }: SelectProps<T>) {
  const [open, setOpen] = useState(false);
  const selected = options.find((o) => o.value === value);
  return (
    <View style={styles.field}>
      {label ? <Text style={styles.label}>{label}</Text> : null}
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={label ? `${label}: ${selected?.label ?? ''}` : selected?.label}
        accessibilityState={{ expanded: open }}
        onPress={() => setOpen((o) => !o)}
        style={[styles.box, { borderColor: open ? color.signal : color.line, justifyContent: 'space-between' }]}
      >
        <Text style={styles.value}>{selected?.label}</Text>
        <ChevronDown size={18} strokeWidth={1.7} color={color.text2} style={{ transform: [{ rotate: open ? '180deg' : '0deg' }] }} />
      </Pressable>
      {open ? (
        <View style={styles.list} accessibilityRole="menu">
          {options.map((option, index) => {
            const isSelected = option.value === value;
            return (
              <Pressable
                key={option.value}
                accessibilityRole="menuitem"
                accessibilityState={{ selected: isSelected }}
                onPress={() => {
                  onChange(option.value);
                  setOpen(false);
                }}
                style={({ pressed }) => [styles.option, index > 0 ? styles.optionDivider : null, { opacity: pressed ? 0.6 : 1 }]}
              >
                <Text style={[styles.value, { color: isSelected ? color.signal : color.text }]}>{option.label}</Text>
                {isSelected ? <Check size={16} strokeWidth={1.7} color={color.signal} /> : null}
              </Pressable>
            );
          })}
        </View>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  field: { gap: space[2] },
  label: { ...(typeScale.caption as TextStyle), color: color.text3 },
  box: {
    minHeight: size.touch + 4,
    borderRadius: radius.button,
    borderWidth: 1,
    backgroundColor: color.surface2,
    paddingHorizontal: space[3],
    flexDirection: 'row',
    alignItems: 'center',
    gap: space[2],
  },
  input: { ...(typeScale.body as TextStyle), lineHeight: undefined, flex: 1, color: color.text, paddingVertical: space[3] },
  value: { ...(typeScale.body as TextStyle), lineHeight: undefined, color: color.text },
  error: { ...(typeScale.caption as TextStyle), color: color.loss },
  list: { borderRadius: radius.button, borderWidth: 1, borderColor: color.line, backgroundColor: color.surface2, overflow: 'hidden' },
  option: {
    minHeight: size.touch,
    paddingHorizontal: space[3],
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  optionDivider: { borderTopWidth: 1, borderTopColor: color.line },
});
