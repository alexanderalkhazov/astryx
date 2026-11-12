// Decimal arithmetic for display previews only (SYSTEM_DESIGN.md §20.1). Authoritative money values always come
// from the API as decimal strings; nothing computed here is ever sent back as a financial value.
import Decimal from 'decimal.js-light';

const D = Decimal.clone({ precision: 40, rounding: Decimal.ROUND_HALF_EVEN });

export type DecimalString = string;

const DECIMAL_PATTERN = /^-?\d+(\.\d+)?$/;

export function isDecimalString(value: string): boolean {
  return DECIMAL_PATTERN.test(value);
}

export function toDecimal(value: DecimalString): Decimal {
  if (!isDecimalString(value)) {
    throw new TypeError(`Expected a decimal string, got "${value}"`);
  }
  return new D(value);
}

/** Display-only product, e.g. capital = quantity × entry while the server re-assesses. */
export function previewMultiply(a: DecimalString, b: DecimalString): DecimalString {
  return toDecimal(a).times(toDecimal(b)).toString();
}

export function previewSubtract(a: DecimalString, b: DecimalString): DecimalString {
  return toDecimal(a).minus(toDecimal(b)).toString();
}

/** Display-only quotient, e.g. a level as a percentage of entry; never sent back to the server. */
export function previewDivide(a: DecimalString, b: DecimalString): DecimalString {
  return toDecimal(a).dividedBy(toDecimal(b)).toString();
}

export { D as PreviewDecimal };
