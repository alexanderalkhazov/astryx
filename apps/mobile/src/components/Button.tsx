import { color, radius, size, space, type as typeScale } from '@astryx/design-tokens';
import type { LucideIcon } from 'lucide-react-native';
import type { ReactNode } from 'react';
import { ActivityIndicator, Pressable, type PressableProps, StyleSheet, Text, type TextStyle, type ViewStyle } from 'react-native';

export type ButtonVariant = 'primary' | 'secondary' | 'destructive' | 'danger' | 'link';

export interface ButtonProps extends Omit<PressableProps, 'children' | 'style'> {
  variant?: ButtonVariant;
  label: string;
  loading?: boolean;
  /** Stretch to the container width (default for primary/secondary/destructive). */
  block?: boolean;
  style?: ViewStyle;
}

interface VariantStyle {
  container: ViewStyle;
  label: TextStyle;
}

// Design 03 · Buttons: primary = light fill, secondary = outline, destructive = red tint, link = signal text.
const variants: Record<ButtonVariant, VariantStyle> = {
  primary: { container: { backgroundColor: color.text }, label: { color: color.bg } },
  secondary: { container: { borderWidth: 1, borderColor: color.lineStrong }, label: { color: color.text } },
  destructive: {
    container: { backgroundColor: 'rgba(255,107,107,0.10)', borderWidth: 1, borderColor: 'rgba(255,107,107,0.45)' },
    label: { color: color.loss },
  },
  // Solid red: the confirming action in a destructive dialog (design: "Close position" in the close dialog).
  danger: { container: { backgroundColor: color.loss }, label: { color: color.bg } },
  link: { container: { minHeight: size.touch, paddingHorizontal: 0 }, label: { color: color.signal } },
};

const disabledStyle: VariantStyle = { container: { backgroundColor: color.surface2, borderWidth: 0 }, label: { color: color.text3 } };

/** Styles for a given state; exported for tests. */
export function buttonStyle(variant: ButtonVariant, state: { pressed: boolean; disabled: boolean }): VariantStyle {
  const base = state.disabled && variant !== 'link' ? disabledStyle : variants[variant];
  return {
    container: { ...base.container, opacity: state.pressed && !state.disabled ? 0.85 : state.disabled && variant === 'link' ? 0.4 : 1 },
    label: base.label,
  };
}

/** One component for every text button in the app (design 03 · Buttons). */
export function Button({ variant = 'primary', label, loading = false, disabled = false, block, style, accessibilityLabel, ...rest }: ButtonProps) {
  const isDisabled = disabled || loading;
  const stretch = block ?? variant !== 'link';
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel ?? label}
      accessibilityState={{ disabled: isDisabled, busy: loading }}
      disabled={isDisabled}
      hitSlop={variant === 'link' ? 8 : undefined}
      style={({ pressed }) => {
        const s = buttonStyle(variant, { pressed, disabled: isDisabled });
        return [styles.container, variant === 'link' ? styles.link : null, stretch ? styles.block : null, s.container, style];
      }}
      {...rest}
    >
      {({ pressed }) =>
        loading ? (
          <ActivityIndicator color={buttonStyle(variant, { pressed, disabled: false }).label.color as string} />
        ) : (
          <Text style={[styles.label, buttonStyle(variant, { pressed, disabled: isDisabled }).label]} numberOfLines={1}>
            {label}
          </Text>
        )
      }
    </Pressable>
  );
}

export interface IconButtonProps extends Omit<PressableProps, 'children' | 'style'> {
  icon: LucideIcon;
  /** Required: icon-only controls must be named for screen readers. */
  accessibilityLabel: string;
  tone?: 'default' | 'signal';
  badge?: ReactNode;
  style?: ViewStyle;
}

/** 44 pt square icon control (design: "44 pt icon button"). */
export function IconButton({ icon: Icon, accessibilityLabel, tone = 'default', disabled, badge, style, ...rest }: IconButtonProps) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel}
      accessibilityState={{ disabled: !!disabled }}
      disabled={disabled}
      style={({ pressed }) => [styles.icon, { opacity: disabled ? 0.4 : pressed ? 0.6 : 1 }, style]}
      {...rest}
    >
      <Icon size={20} strokeWidth={1.7} color={tone === 'signal' ? color.signal : color.text} />
      {badge}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  container: {
    minHeight: size.button,
    borderRadius: radius.button,
    paddingHorizontal: space[4],
    alignItems: 'center',
    justifyContent: 'center',
    flexDirection: 'row',
  },
  block: { alignSelf: 'stretch' },
  link: { alignSelf: 'flex-start', minHeight: size.touch },
  label: { ...(typeScale.section as TextStyle) },
  icon: {
    width: size.touch,
    height: size.touch,
    borderRadius: radius.button,
    backgroundColor: color.surface2,
    alignItems: 'center',
    justifyContent: 'center',
  },
});
