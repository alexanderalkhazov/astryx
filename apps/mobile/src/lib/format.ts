// Formatting of financial values for display (SYSTEM_DESIGN.md §8.1, §20.1).
// Inputs are decimal strings from the API; values never pass through JavaScript numbers.
// Negative values use the real minus sign (U+2212). P&L always carries a sign, never colour alone.
import { toDecimal, type DecimalString } from './decimal';

export const MINUS = '\u2212';

type Sign = '+' | typeof MINUS | '';

function signOf(isNegative: boolean, isZero: boolean, forceSign: boolean): Sign {
  if (isZero) return '';
  if (isNegative) return MINUS;
  return forceSign ? '+' : '';
}

function groupThousands(integerDigits: string): string {
  return integerDigits.replace(/\B(?=(\d{3})+(?!\d))/g, ',');
}

/** Rounds half-even to `dp` places and returns sign + grouped absolute value parts. */
function fixed(value: DecimalString, dp: number, forceSign: boolean): { sign: Sign; whole: string; fraction: string } {
  const rounded = toDecimal(value).toDecimalPlaces(dp);
  const absolute = rounded.abs().toFixed(dp);
  const [whole = '0', fraction = ''] = absolute.split('.');
  return { sign: signOf(rounded.isNegative(), rounded.isZero(), forceSign), whole: groupThousands(whole), fraction };
}

export interface MoneyOptions {
  /** Show "+" on positive values (P&L). Negative values always show "−". */
  signed?: boolean;
  dp?: number;
}

/** "-630.8" → "−$630.80"; "1330" (signed) → "+$1,330.00"; "14174.00" → "$14,174.00". */
export function formatMoney(value: DecimalString, { signed = false, dp = 2 }: MoneyOptions = {}): string {
  const { sign, whole, fraction } = fixed(value, dp, signed);
  return `${sign}$${whole}${dp > 0 ? `.${fraction}` : ''}`;
}

/** Profit or loss: always signed. */
export function formatPnl(value: DecimalString): string {
  return formatMoney(value, { signed: true });
}

/** Hero value split so cents can be dimmed (design: "Hero value with dimmed cents"). */
export function splitMoney(value: DecimalString): { main: string; cents: string } {
  const { sign, whole, fraction } = fixed(value, 2, false);
  return { main: `${sign}$${whole}`, cents: `.${fraction}` };
}

/** Per-share price: 2 dp at or above $1, 4 dp below (price tick, §20.1). "186.5" → "186.50". */
export function formatPrice(value: DecimalString): string {
  const decimal = toDecimal(value);
  const dp = decimal.abs().greaterThanOrEqualTo(1) ? 2 : 4;
  const { sign, whole, fraction } = fixed(value, dp, false);
  return `${sign}${whole}.${fraction}`;
}

export interface PercentOptions {
  signed?: boolean;
  dp?: number;
}

/** Ratios are fractions: "0.0342" (signed) → "+3.42%"; "0.0075" → "0.75%"; "-0.0445" → "−4.45%". */
export function formatPct(fraction: DecimalString, { signed = false, dp = 2 }: PercentOptions = {}): string {
  const percent = toDecimal(fraction).times(100).toString();
  const { sign, whole, fraction: digits } = fixed(percent, dp, signed);
  return `${sign}${whole}${dp > 0 ? `.${digits}` : ''}%`;
}

/** Share quantity: "76" → "76", "1500" → "1,500", "0.5" → "0.5". */
export function formatQuantity(value: DecimalString): string {
  const decimal = toDecimal(value);
  const [whole = '0', fraction] = decimal.abs().toString().split('.');
  return `${decimal.isNegative() ? MINUS : ''}${groupThousands(whole)}${fraction ? `.${fraction}` : ''}`;
}

/** Reward:risk ratio "2.108" → "2.11". */
export function formatRatio(value: DecimalString, dp = 2): string {
  const { sign, whole, fraction } = fixed(value, dp, false);
  return `${sign}${whole}.${fraction}`;
}

/**
 * Share volume, which is a count (not money), so a JS number is fine here:
 * 412883000 → "412.8M", 2400 → "2.4K".
 */
export function formatVolume(shares: number): string {
  const units: [number, string][] = [[1e12, 'T'], [1e9, 'B'], [1e6, 'M'], [1e3, 'K']];
  for (const [size, suffix] of units) {
    if (Math.abs(shares) >= size) {
      return `${(Math.trunc((shares / size) * 10) / 10).toFixed(1)}${suffix}`;
    }
  }
  return String(shares);
}

/** Spoken form for screen readers: "−$630.80" → "minus 630 dollars and 80 cents". */
export function moneyAccessibilityLabel(value: DecimalString): string {
  const { sign, whole, fraction } = fixed(value, 2, true);
  const words = sign === MINUS ? 'minus ' : sign === '+' ? 'plus ' : '';
  return `${words}${whole.replace(/,/g, '')} dollars and ${Number.parseInt(fraction, 10)} cents`;
}

export type Direction = 'up' | 'down' | 'unchanged';

/** Direction of a signed change ("0.0342" → up), for tone and screen-reader wording. */
export function changeDirection(change: DecimalString): Direction {
  const value = toDecimal(change);
  return value.isZero() ? 'unchanged' : value.isNegative() ? 'down' : 'up';
}
