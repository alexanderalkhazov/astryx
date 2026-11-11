import { color, radius, size, space, type as typeScale } from '@astryx/design-tokens';
import { Minus, Plus } from 'lucide-react-native';
import { Pressable, StyleSheet, Text, type TextStyle, View } from 'react-native';

import { formatQuantity } from '@/lib/format';
import { toDecimal, type DecimalString } from '@/lib/decimal';

export interface StepperProps {
  /** Decimal string, e.g. "76". */
  value: DecimalString;
  min?: DecimalString;
  max?: DecimalString;
  step?: DecimalString;
  unit: string;
  onChange: (value: DecimalString) => void;
  disabled?: boolean;
}

/** Quantity stepper (design 03 · "76 shares · stepper"). Arithmetic is decimal; bounds disable the buttons. */
export function Stepper({ value, min = '0', max, step = '1', unit, onChange, disabled = false }: StepperProps) {
  const current = toDecimal(value);
  const canDecrement = !disabled && current.minus(toDecimal(step)).greaterThanOrEqualTo(toDecimal(min));
  const canIncrement = !disabled && (max === undefined || current.plus(toDecimal(step)).lessThanOrEqualTo(toDecimal(max)));
  const decrement = () => canDecrement && onChange(current.minus(toDecimal(step)).toString());
  const increment = () => canIncrement && onChange(current.plus(toDecimal(step)).toString());

  return (
    <View
      style={styles.row}
      accessible
      accessibilityRole="adjustable"
      accessibilityLabel={unit}
      accessibilityValue={{ text: `${formatQuantity(value)} ${unit}` }}
      accessibilityActions={[{ name: 'increment' }, { name: 'decrement' }]}
      onAccessibilityAction={(event) => (event.nativeEvent.actionName === 'increment' ? increment() : decrement())}
    >
      <StepButton label={`Decrease ${unit}`} disabled={!canDecrement} onPress={decrement} icon="minus" />
      <View style={styles.value}>
        <Text style={styles.number}>{formatQuantity(value)}</Text>
        <Text style={styles.unit}>{unit}</Text>
      </View>
      <StepButton label={`Increase ${unit}`} disabled={!canIncrement} onPress={increment} icon="plus" />
    </View>
  );
}

function StepButton({ label, disabled, onPress, icon }: { label: string; disabled: boolean; onPress: () => void; icon: 'minus' | 'plus' }) {
  const Icon = icon === 'minus' ? Minus : Plus;
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityState={{ disabled }}
      disabled={disabled}
      onPress={onPress}
      style={({ pressed }) => [styles.button, { opacity: disabled ? 0.35 : pressed ? 0.6 : 1 }]}
    >
      <Icon size={18} strokeWidth={1.7} color={color.text} />
    </Pressable>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: space[3] },
  button: {
    width: size.touch,
    height: size.touch,
    borderRadius: radius.button,
    borderWidth: 1,
    borderColor: color.lineStrong,
    alignItems: 'center',
    justifyContent: 'center',
  },
  value: { alignItems: 'center', flex: 1 },
  number: { ...(typeScale.priceXl as TextStyle), fontSize: 24, color: color.text },
  unit: { ...(typeScale.caption as TextStyle), color: color.text3 },
});
