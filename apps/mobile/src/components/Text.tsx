import { color, type as typeScale } from '@astryx/design-tokens';
import type { ReactNode } from 'react';
import { Text as RNText, type TextProps as RNTextProps, type TextStyle } from 'react-native';

import { formatPnl, moneyAccessibilityLabel } from '@/lib/format';
import { toDecimal, type DecimalString } from '@/lib/decimal';

export type TextTone = 'primary' | 'secondary' | 'tertiary' | 'gain' | 'loss' | 'signal' | 'pending';

const toneColor: Record<TextTone, string> = {
  primary: color.text,
  secondary: color.text2,
  tertiary: color.text3,
  gain: color.gain,
  loss: color.loss,
  signal: color.signal,
  pending: color.pending,
};

export type TextVariant = keyof typeof typeScale;

export interface TextProps extends RNTextProps {
  variant?: TextVariant;
  tone?: TextTone;
  children?: ReactNode;
}

/** All app text goes through this component so type and colour always come from the design tokens. */
export function Text({ variant = 'body', tone = 'primary', style, ...rest }: TextProps) {
  return <RNText style={[typeScale[variant] as TextStyle, { color: toneColor[tone] }, style]} {...rest} />;
}

function variantOf(variant: TextVariant) {
  return function Variant(props: Omit<TextProps, 'variant'>) {
    return <Text variant={variant} {...props} />;
  };
}

Text.Display = variantOf('display');
Text.PriceXl = variantOf('priceXl');
Text.Title = variantOf('title');
Text.Headline = variantOf('headline');
Text.Section = variantOf('section');
Text.Body = variantOf('body');
Text.Data = variantOf('data');
Text.DataSm = variantOf('dataSm');
Text.Caption = variantOf('caption');
Text.Overline = variantOf('overline');

export interface PnlProps extends Omit<TextProps, 'children' | 'tone'> {
  value: DecimalString;
}

/**
 * Profit or loss: always signed (+/−) and coloured green/red, so meaning never depends on colour alone.
 * Zero is neutral. Screen readers hear "plus/minus … dollars".
 */
export function Pnl({ value, variant = 'data', ...rest }: PnlProps) {
  const decimal = toDecimal(value);
  const tone: TextTone = decimal.isZero() ? 'secondary' : decimal.isNegative() ? 'loss' : 'gain';
  return (
    <Text variant={variant} tone={tone} accessibilityLabel={moneyAccessibilityLabel(value)} {...rest}>
      {formatPnl(value)}
    </Text>
  );
}
