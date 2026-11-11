import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import { previewMultiply, previewSubtract } from '../decimal';
import {
  MINUS,
  formatMoney,
  formatPct,
  formatPnl,
  formatPrice,
  formatQuantity,
  formatRatio,
  formatVolume,
  moneyAccessibilityLabel,
  splitMoney,
} from '../format';

describe('formatMoney', () => {
  it('uses a real minus sign and two decimals (T-030 acceptance)', () => {
    expect(formatMoney('-630.8')).toBe('−$630.80');
    expect(formatMoney('-630.8')).not.toContain('-');
  });

  it('groups thousands', () => {
    expect(formatMoney('14174')).toBe('$14,174.00');
    expect(formatMoney('118181.30')).toBe('$118,181.30');
    expect(formatMoney('0.5')).toBe('$0.50');
  });

  it('rounds half-even at the cent', () => {
    expect(formatMoney('2.345')).toBe('$2.34');
    expect(formatMoney('2.355')).toBe('$2.36');
  });

  it('keeps precision a JS number would lose', () => {
    expect(formatMoney('12345678901234567.89')).toBe('$12,345,678,901,234,567.89');
    expect(formatMoney('0.1')).toBe('$0.10'); // 0.1 + 0.2 style float noise never appears
  });

  it('rejects anything that is not a decimal string', () => {
    expect(() => formatMoney('1e5')).toThrow(TypeError);
    expect(() => formatMoney('12,5')).toThrow(TypeError);
    expect(() => formatMoney('')).toThrow(TypeError);
  });
});

describe('formatPnl', () => {
  it('always carries a sign, matching the design examples', () => {
    expect(formatPnl('1330')).toBe('+$1,330.00');
    expect(formatPnl('-630.80')).toBe(`${MINUS}$630.80`);
    expect(formatPnl('484.13')).toBe('+$484.13');
    expect(formatPnl('-9.12')).toBe(`${MINUS}$9.12`);
  });

  it('shows zero without a sign', () => {
    expect(formatPnl('0')).toBe('$0.00');
    expect(formatPnl('-0.001')).toBe('$0.00');
  });
});

describe('splitMoney', () => {
  it('splits the hero value so cents can be dimmed', () => {
    expect(splitMoney('118181.30')).toEqual({ main: '$118,181', cents: '.30' });
    expect(splitMoney('-42.5')).toEqual({ main: `${MINUS}$42`, cents: '.50' });
  });
});

describe('formatPrice', () => {
  it('uses the price tick: 2 dp at or above $1, 4 dp below', () => {
    expect(formatPrice('186.5')).toBe('186.50');
    expect(formatPrice('186.40')).toBe('186.40');
    expect(formatPrice('0.123456')).toBe('0.1235');
    expect(formatPrice('1234.5')).toBe('1,234.50');
  });
});

describe('formatPct', () => {
  it('formats fractions as percentages', () => {
    expect(formatPct('0.0075')).toBe('0.75%');
    expect(formatPct('0.12', { dp: 0 })).toBe('12%');
    expect(formatPct('0.0342', { signed: true })).toBe('+3.42%');
    expect(formatPct('-0.0445', { signed: true })).toBe(`${MINUS}4.45%`);
    expect(formatPct('-0.02')).toBe(`${MINUS}2.00%`);
  });
});

describe('quantities, ratios and volume', () => {
  it('formats share quantities', () => {
    expect(formatQuantity('76')).toBe('76');
    expect(formatQuantity('1500')).toBe('1,500');
    expect(formatQuantity('0.5')).toBe('0.5');
  });

  it('formats reward:risk', () => {
    expect(formatRatio('2.108433734939759')).toBe('2.11');
  });

  it('formats volume counts compactly', () => {
    expect(formatVolume(412_883_000)).toBe('412.8M');
    expect(formatVolume(2_400)).toBe('2.4K');
    expect(formatVolume(950)).toBe('950');
  });
});

describe('accessibility', () => {
  it('speaks the sign and the amount', () => {
    expect(moneyAccessibilityLabel('-630.80')).toBe('minus 630 dollars and 80 cents');
    expect(moneyAccessibilityLabel('1330')).toBe('plus 1330 dollars and 0 cents');
  });
});

describe('preview arithmetic', () => {
  it('multiplies and subtracts exactly (design: 76 × 186.50 = 14,174.00)', () => {
    expect(formatMoney(previewMultiply('76', '186.50'))).toBe('$14,174.00');
    expect(formatMoney(previewSubtract('21840.55', '14175.52'))).toBe('$7,665.03');
    expect(previewMultiply('0.1', '3')).toBe('0.3');
  });
});

describe('no floating point in formatters (SYSTEM_DESIGN.md §20.1)', () => {
  it('format.ts never converts money strings with Number(), parseFloat or unary plus', () => {
    const source = readFileSync(join(__dirname, '..', 'format.ts'), 'utf8')
      .split('\n')
      .filter((line) => !line.trim().startsWith('//') && !line.trim().startsWith('*'))
      .join('\n');
    const allowed = source.replace(/Number\.parseInt\(fraction, 10\)/g, ''); // cents count in a spoken label
    expect(allowed).not.toMatch(/\bNumber\(|parseFloat|\(\s*\+\s*value|\+value\b/);
  });
});
